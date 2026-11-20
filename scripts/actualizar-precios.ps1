<#
.SYNOPSIS
  Descarga el dump de SEPA, genera los datos de la app y los publica en GitHub Pages.

.DESCRIPTION
  El portal de SEPA bloquea los servidores de GitHub, así que la descarga se hace desde esta PC:
  1. Descarga el dump de hoy (si todavía no salió, el de ayer) y genera version/catalog/prices.json.
  2. Si son más nuevos que los publicados, los sube a la rama "datos" del repositorio (un solo commit que
     se reemplaza cada vez, para no llenar el historial).
  3. Lanza el workflow "Publicar app y precios", que compila la app y la publica con esos datos.

  Usa la sesión de GitHub CLI (gh) de este usuario. Deja un registro en %LOCALAPPDATA%\Super\actualizar-precios.log.
  Lo corre la tarea programada "Super - actualizar precios" (scripts\instalar-tarea.ps1).

.PARAMETER Force
  Publica aunque los datos no sean más nuevos que los publicados.
#>
param(
    [switch] $Force,
    [string] $Repo = 'Renacli2/super-laplata',
    [string] $SiteUrl = 'https://renacli2.github.io/super-laplata'
)

# 'Continue' a propósito: en Windows PowerShell 5.1, si git o dotnet escriben en stderr (por ejemplo "remote: ..."),
# con 'Stop' eso corta el script aunque el comando haya funcionado. Los errores se detectan con $LASTEXITCODE.
$ErrorActionPreference = 'Continue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$logDir = Join-Path $env:LOCALAPPDATA 'Super'
$log = Join-Path $logDir 'actualizar-precios.log'
New-Item -ItemType Directory -Force $logDir | Out-Null

function Write-Log([string] $message) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $message"
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding UTF8
}

function Get-ArgentinaToday {
    [DateTime]::UtcNow.AddHours(-3).Date
}

$dayNames = @{ Sunday = 'domingo'; Monday = 'lunes'; Tuesday = 'martes'; Wednesday = 'miercoles';
               Thursday = 'jueves'; Friday = 'viernes'; Saturday = 'sabado' }

function Export-Day([DateTime] $day, [string] $output) {
    $name = $dayNames[$day.DayOfWeek.ToString()]
    Write-Log "Descargando el dump del $name..."
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    & dotnet run -c Release --project (Join-Path $root 'tools\Super.Export') -- --download $output $name 2>&1 |
        ForEach-Object { Write-Log "   $_" }
    if ($LASTEXITCODE -ne 0) { throw "Super.Export falló para el $name (código $LASTEXITCODE)" }
    [DateTime]::ParseExact((Get-Content (Join-Path $output 'version.json') -Raw -ErrorAction Stop | ConvertFrom-Json).date, 'yyyy-MM-dd', $null)
}

try {
    $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
    if (-not $gh) { throw 'Falta GitHub CLI (gh). Instalalo e iniciá sesión con: gh auth login' }
    & $gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI no tiene sesión iniciada: corré gh auth login' }

    $published = $null
    try {
        $v = Invoke-RestMethod "$SiteUrl/data/version.json?t=$([DateTime]::UtcNow.Ticks)" -TimeoutSec 30 -ErrorAction Stop
        $published = [DateTime]::ParseExact($v.date, 'yyyy-MM-dd', $null)
        Write-Log "Publicado: precios del $($published.ToString('yyyy-MM-dd'))"
    } catch {
        Write-Log 'No se pudo leer la versión publicada; se publica igual.'
    }

    $today = Get-ArgentinaToday
    if (-not $Force -and $published -eq $today) {
        Write-Log 'Los precios de hoy ya están publicados.'
        return
    }

    $work = Join-Path $env:TEMP 'super-precios'
    $data = Join-Path $work 'data'
    $date = Export-Day $today $data

    # Antes de ~13:20 el archivo del día todavía es el de la semana pasada: probar con el de ayer.
    if ($date -lt $today.AddDays(-1)) {
        Write-Log "El dump de hoy todavía no salió (trae precios del $($date.ToString('yyyy-MM-dd'))); se prueba con el de ayer."
        $date = Export-Day $today.AddDays(-1) $data
    }

    if (-not $Force -and $published -and $date -le $published) {
        Write-Log "No hay precios nuevos (descargados: $($date.ToString('yyyy-MM-dd'))). No se publica nada."
        return
    }

    Write-Log "Subiendo los datos del $($date.ToString('yyyy-MM-dd')) a la rama datos..."
    Push-Location $data
    try {
        if (Test-Path '.git') { Remove-Item '.git' -Recurse -Force }
        & git init -q -b datos
        & git add version.json catalog.json prices.json
        & git commit -q -m "Precios del $($date.ToString('yyyy-MM-dd'))"
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el commit de datos' }
        & git -c credential.helper= -c "credential.helper=!'$($gh -replace '\\', '/')' auth git-credential" `
            push --force --quiet "https://github.com/$Repo.git" datos 2>&1 | ForEach-Object { Write-Log "   $_" }
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo subir la rama datos' }
    } finally {
        Pop-Location
    }

    & $gh workflow run publicar.yml --repo $Repo --ref main 2>&1 | ForEach-Object { Write-Log "   $_" }
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo lanzar el workflow de publicación' }
    Write-Log "Listo: en unos minutos la app muestra los precios del $($date.ToString('yyyy-MM-dd'))."
}
catch {
    Write-Log "ERROR: $($_.Exception.Message)"
    exit 1
}
