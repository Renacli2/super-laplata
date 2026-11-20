# Super — comparador de carritos de supermercado (La Plata)

Solución .NET 10. La app publicada no usa servidor ni base de datos: es una app instalable (PWA) en GitHub Pages
que descarga los productos y precios del día como archivos, y todo lo demás pasa en el celular.

## Cómo se usa

- **Miembros:** buscan productos (por nombre o escaneando el código), arman su pedido y tocan **Enviar pedido**:
  se comparte un link por WhatsApp (o donde sea) a quien hace las compras. No ven precios.
- **Administrador** (quien hace las compras; se activa en *Ajustes* de su celular): abre los links que le mandan
  y los suma a su lista, con quién pidió cada cosa. Antes de salir toca **Comparar precios**: la app le dice si
  conviene comprar todo en un supermercado o repartir, y qué llevar en cada uno, con casillas para ir tachando.

## Qué hay

| Proyecto | Contenido |
|---|---|
| `src/Super.Domain` | `Chain` (Carrefour, Changomás, COTO), `Ean` (normalización a 13 dígitos, dígito de control, códigos de peso variable) |
| `src/Super.Application` | `ComparisonEngine` (umbral 3 %), `CatalogSearch` (búsqueda sin acentos), `OrderLink` (pedidos en un link) |
| `src/Super.Infrastructure` | Importador y descarga de SEPA (`Sepa/`), exportador de datos estáticos (`Export/`), PostgreSQL con EF Core (`Persistence/`), ingesta (`Ingestion/`) |
| `src/Super.Contracts` | Formato de los archivos de datos y de la API |
| `src/Super.Web` | App Blazor WebAssembly instalable (PWA) |
| `src/Super.Api` | API sobre PostgreSQL (opcional: para consultas en la PC; la app publicada no la usa) |
| `tests/Super.Tests` | xUnit: EAN, motor, búsqueda, links de pedido, importador, exportador, integración con PostgreSQL y API |
| `tools/Super.Export` | Genera los archivos de datos de la app desde el dump (o lo descarga) |
| `tools/Super.Ingest`, `tools/Super.SepaProbe` | Consolas para la base PostgreSQL local y para probar el importador |

## Datos de la app

`Super.Export` lee el dump diario de SEPA y genera tres archivos (con el dump del 13/11/2026: 43.319 productos):

| Archivo | Qué tiene | Tamaño (comprimido) | Lo baja |
|---|---|---|---|
| `data/version.json` | fecha de los precios | < 1 KB | todos |
| `data/catalog.json` | código, descripción, marca y presentación | ~630 KB | todos, para buscar |
| `data/prices.json` | precio de lista mínimo de cada cadena en La Plata | ~280 KB | solo el admin, al comparar |

```bash
dotnet run --project tools/Super.Export -- C:\ruta\sepa_viernes.zip src/Super.Web/wwwroot/data
dotnet run --project tools/Super.Export -- --download src/Super.Web/wwwroot/data          # baja el dump de hoy
```

El service worker guarda la última copia de los datos: sin señal, la app busca y compara con los últimos precios.

## Publicación: GitHub Pages (gratis)

La app está en https://renacli2.github.io/super-laplata/.

**El dump lo descarga la PC**, porque el portal de SEPA bloquea a los servidores de GitHub (responde 403 desde el
17/11/2026; desde una conexión argentina funciona):

1. La tarea programada **"Super - actualizar precios"** corre [`scripts/actualizar-precios.ps1`](scripts/actualizar-precios.ps1)
   a las 15:00 y a las 20:00 (el dump del día sale cerca de las 13:20). Si la PC estaba apagada, corre al prenderla.
   Descarga el dump de hoy (o el de ayer, si el de hoy todavía no salió) y genera los datos.
2. Si son más nuevos que los publicados, los sube a la rama **`datos`** (un solo commit que se reemplaza cada vez)
   y lanza el workflow [`.github/workflows/publicar.yml`](.github/workflows/publicar.yml), que corre los tests,
   compila la app y la publica con esos datos. El workflow también corre con cada push a `main`.
3. Si la PC no corre la tarea, la app sigue con los últimos precios publicados (la fecha se ve al pie).

Instalar o actualizar la tarea: `scripts\instalar-tarea.ps1` (necesita `dotnet`, `git` y GitHub CLI con sesión iniciada).
Registro: `%LOCALAPPDATA%\Super\actualizar-precios.log`; salida completa de la última corrida: `ultima-corrida.txt` en la
misma carpeta. A mano: `scripts\actualizar-precios.ps1` (con `-Force` publica aunque no haya precios nuevos).

Requisitos de GitHub: el repositorio tiene que ser **público** (GitHub Pages gratis) y en *Settings → Pages*,
*Source: GitHub Actions*.

**Instalar:** Android (Chrome o Edge): menú ⋮ → *Instalar aplicación*. iPhone (Safari): Compartir → *Agregar a inicio*.
Windows (Edge o Chrome): ícono de instalar en la barra de direcciones.

**Escáner:** usa `BarcodeDetector`, que tienen Chrome y Edge en Android. En iPhone y en Windows el botón no aparece
y el código se escribe en el buscador.

**En iPhone**, la app instalada en la pantalla de inicio guarda sus datos aparte de Safari: conviene abrir los links de
pedido desde la app (copiar y pegar en el navegador de la app) o usar siempre Safari.

## Probar en la PC

```bash
dotnet run --project tools/Super.Export -- C:\ruta\sepa_viernes.zip src/Super.Web/wwwroot/data
dotnet run --project src/Super.Web --launch-profile http
```

y abrir http://localhost:5039 (la carpeta `wwwroot/data` no se versiona).

Probado el 15/11/2026: 98 tests; flujo completo en el navegador integrado simulando GitHub Pages (subcarpeta `/super-laplata/`,
`404.html` para las rutas): búsqueda sin acentos, pedido de un miembro con nombre, link generado, admin que recibe dos
pedidos y los une, comparación con los precios reales del 13/11. El navegador integrado no registra service workers;
eso se verificó antes en Edge.

## Base de datos

PostgreSQL 17 + EF Core 10 (Npgsql), nombres en snake_case. Tablas:

| Tabla | Clave | Contenido |
|---|---|---|
| `products` | `ean` | Catálogo por EAN canónico (descripción, marca, presentación, primera/última vez visto) |
| `branches` | `id` (único por cadena + `id_bandera` + `id_sucursal`) | Sucursales de La Plata |
| `branch_prices` | fecha + sucursal + EAN | Precio de lista, de referencia y promo por sucursal y día |
| `chain_prices` | fecha + cadena + EAN | Precio mínimo de lista de la cadena en La Plata: lo que lee el comparador |
| `import_runs` | `id` (único por fecha + cadena) | Una fila por ingesta, con las estadísticas del importador |

La conexión se toma de la variable `SUPER_DB` o de `appsettings.Local.json` (no se versiona):

```json
{ "ConnectionStrings": { "Super": "Host=localhost;Port=5432;Database=super;Username=postgres;Password=..." } }
```

Los tests de integración crean y borran una base temporal `super_test_*` en ese mismo servidor
(o en el de `SUPER_TEST_DB`); si no hay servidor, se omiten.

## Ingesta

```bash
dotnet run --project tools/Super.Ingest -- import <sepa_viernes.zip | carpeta | zip de un comercio>
dotnet run --project tools/Super.Ingest -- status
dotnet run --project tools/Super.Ingest -- compare 7790070035950 7791293045955x2
```

- Acepta el zip diario de SEPA tal como se descarga (con los zips por comercio adentro), una carpeta o un zip de comercio.
  Solo extrae Carrefour (10), Changomás (11) y COTO (12). La fecha sale del nombre del archivo.
- Aplica las migraciones pendientes antes de importar.
- Es idempotente: reimportar la misma cadena y fecha reemplaza esos precios, dentro de una transacción.
- El comparador usa el último día importado de cada cadena.

Con el dump del 13/11/2026 tarda unos 30 s y deja 43.319 productos, 68.944 precios por sucursal
y 55.175 precios por cadena.

Migraciones nuevas: `dotnet ef migrations add <Nombre> --project src/Super.Infrastructure --output-dir Persistence/Migrations`
(`dotnet tool restore` instala `dotnet-ef`).

## Primeros pasos

```bash
dotnet test
```

La solución es `Super.slnx`. `global.json` fija el SDK 10.0.x y `nuget.config` deja nuget.org como único
origen (ignora el mapeo de paquetes de la config global de NuGet de la máquina).

Probar con datos reales (un `comercio-sepa-NN` descomprimido del dump diario):

```bash
dotnet run --project tools/Super.SepaProbe -- 12 <carpeta>/sucursales.csv <carpeta>/productos.csv
```

Resultado esperado con el dump del 13/11/2026 (verificado con un script Python equivalente):
Carrefour 36.612 filas en La Plata / 21.613 EAN, Changomás 27.557 / 27.557, COTO 7.588 / 6.985
(antes de descartar precios fuera de rango).

Resultado real de `Super.SepaProbe` con ese dump (15/11/2026):

| Cadena | Filas en La Plata | Descartadas (código interno `2…`) | Aceptadas | EAN distintos |
|---|---:|---:|---:|---:|
| Carrefour | 36.612 | 935 sin EAN + 685 con prefijo 2 | 34.992 | 21.223 |
| Changomás | 27.557 | 590 con prefijo 2 | 26.967 | 26.967 |
| COTO | 7.588 | 603 sin EAN | 6.985 | 6.985 |

La diferencia con las cifras de arriba son los códigos internos de 13 dígitos que empiezan con 2
(fiambres, verdulería, tarjetas de regalo) marcados como EAN en SEPA: el importador los descarta a propósito.
Los precios de $0,10 de Changomás están todos en esos códigos, por eso "precio fuera de rango" da 0.

## Reglas implementadas

- El EAN real está en `id_producto`; `productos_ean` es solo un indicador (1 = EAN).
- SEPA rellena con ceros a 13 dígitos; la cámara lee el código original → siempre se compara normalizado.
- Códigos de 13 dígitos que empiezan con `2` = peso variable → ítems libres, no se comparan.
- Precio de lista, mínimo entre las sucursales de La Plata (las promociones todavía no se aplican).
- Brecha = (mejor total en una sola cadena − total repartido) / total repartido; si ≤ umbral → cadena única.
- Una cadena solo puede ser "única" si tiene todos los ítems disponibles del carrito.

## Pendiente (siguientes fases)

Crear el repositorio público en GitHub y activar Pages, aplicar promociones, clasificación por categoría,
historial de precios, escáner para iPhone y Windows, listas compartidas en tiempo real (necesitaría un servidor).

## Estado de compilación

15/11/2026: compila sin advertencias con el SDK 10.0.301 y pasan los 98 tests (19 de integración con PostgreSQL 17, que se omiten sin servidor).
`Super.SepaProbe` probado contra el dump real del 13/11/2026 (ver tabla arriba).
