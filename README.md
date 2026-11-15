# Super — comparador de carritos de supermercado (La Plata)

Solución .NET 10. Estado: importador, base de datos, ingesta, API y app instalable (PWA) para celular y Windows.

## Qué hay

| Proyecto | Contenido |
|---|---|
| `src/Super.Domain` | `Chain` (Carrefour, Changomás, COTO), `Ean` (normalización a 13 dígitos, dígito de control, códigos de peso variable) |
| `src/Super.Application` | `ComparisonEngine`: precio mínimo por ítem, total por cadena, regla del umbral (3 % por defecto). `IPriceQuoteSource` |
| `src/Super.Infrastructure` | Importador de SEPA (`Sepa/`), lector del dump (`SepaDump`), base PostgreSQL con EF Core (`Persistence/`), ingesta (`Ingestion/`) |
| `src/Super.Contracts` | Contratos JSON entre la API y la app |
| `src/Super.Api` | API ASP.NET Core (búsqueda, producto por código, comparación, estado). Sirve también la app |
| `src/Super.Web` | App Blazor WebAssembly instalable (PWA): lista, búsqueda, escáner de códigos, resultado |
| `tests/Super.Tests` | xUnit: EAN, motor, importador, lector del dump, integración con PostgreSQL y API |
| `tools/Super.SepaProbe` | Consola para probar el importador con los CSV de un comercio, sin base |
| `tools/Super.Ingest` | Consola: migraciones, ingesta del dump, estado y comparación de un carrito contra la base |

## App (celular y Windows)

Es una sola app web instalable (PWA): el mismo código se instala en Android, iPhone y Windows desde el navegador.
La API la sirve en el mismo sitio, así que hay un solo programa para publicar.

Correrla en la PC:

```bash
dotnet run --project src/Super.Api --launch-profile http
```

y abrir http://localhost:5234. En Windows, Edge o Chrome muestran "Instalar aplicación" en la barra de direcciones
(en `localhost` funciona sin HTTPS).

Versión de producción:

```bash
dotnet publish src/Super.Api -c Release -o publish
```

`publish/` contiene `Super.Api.exe` (~35 MB) y requiere el runtime de ASP.NET Core 10 instalado. Necesita la conexión a PostgreSQL en
`ConnectionStrings__Super` (variable de entorno) y escuchar detrás de HTTPS.

**Instalar en el celular:** el navegador solo instala la app (y solo deja usar la cámara) si se abre con **HTTPS** desde un
dominio. Con `http://IP-de-la-PC:puerto` en la misma red Wi-Fi se puede usar, pero no instalar ni escanear.
Una vez publicada con HTTPS:

- Android (Chrome o Edge): menú ⋮ → "Instalar aplicación".
- iPhone (Safari): Compartir → "Agregar a inicio".
- Windows (Edge o Chrome): ícono de instalar en la barra de direcciones.

**Escáner:** usa `BarcodeDetector`, que hoy tienen Chrome y Edge en Android. En iPhone y en Windows el botón no aparece
y el código se escribe en el buscador (acepta el número tal como figura debajo de las barras).

**Sin conexión:** la app (pantallas y lista guardada) abre sin internet; buscar y comparar necesitan conexión.

Probado el 15/11/2026: 68 tests; app en el navegador integrado (búsqueda, lista, comparación, tildado, lista guardada al
recargar, vista de celular y escritorio, tema claro y oscuro); versión publicada en Edge, con el service worker activo.
El navegador integrado de Claude no registra service workers, por eso esa parte se verificó en Edge.

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

Publicar con HTTPS (hosting a definir), descarga automática del dump y programación diaria de la ingesta,
aplicar promociones, búsqueda sin acentos (unaccent),
clasificación por categoría, login con Google, grupos y listas compartidas, escáner para iPhone y Windows.

## Estado de compilación

15/11/2026: compila sin advertencias con el SDK 10.0.301 y pasan los 68 tests (17 de integración con PostgreSQL 17).
`Super.SepaProbe` probado contra el dump real del 13/11/2026 (ver tabla arriba).
