<#
.SYNOPSIS
  Registra (o actualiza) la tarea programada que publica los precios todos los días.

.DESCRIPTION
  Tarea "Super - actualizar precios": corre scripts\actualizar-precios.ps1 a las 15:00 y a las 20:00
  (el portal de SEPA sube el dump del día cerca de las 13:20). Si la PC estaba apagada a esa hora,
  corre en cuanto se prende. Corre solo con la sesión de Windows iniciada y en una ventana oculta.

  Para quitarla:  Unregister-ScheduledTask -TaskName 'Super - actualizar precios'
#>
$ErrorActionPreference = 'Stop'
$taskName = 'Super - actualizar precios'
$script = Join-Path $PSScriptRoot 'actualizar-precios.ps1'

# conhost --headless: corre sin ninguna ventana (con -WindowStyle Hidden igual aparece una consola un instante,
# y si alguien la cierra se corta la descarga). La salida completa de la última corrida queda en ultima-corrida.txt.
$outFile = Join-Path $env:LOCALAPPDATA 'Super\ultima-corrida.txt'
$action = New-ScheduledTaskAction -Execute 'conhost.exe' `
    -Argument "--headless cmd.exe /c powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$script`" > `"$outFile`" 2>&1" `
    -WorkingDirectory (Split-Path $PSScriptRoot)
$triggers = @(
    New-ScheduledTaskTrigger -Daily -At '15:00'
    New-ScheduledTaskTrigger -Daily -At '20:00'
)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -RunOnlyIfNetworkAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Settings $settings `
    -Description 'Descarga el dump de SEPA y publica los precios de la app Super en GitHub Pages.' -Force | Out-Null

$task = Get-ScheduledTask -TaskName $taskName
$info = $task | Get-ScheduledTaskInfo
Write-Host "Tarea '$taskName' registrada. Próxima corrida: $($info.NextRunTime)"
Write-Host "Registro: $env:LOCALAPPDATA\Super\actualizar-precios.log"
