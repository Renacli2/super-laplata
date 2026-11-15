using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Super.Application.Comparison;
using Super.Domain;
using Super.Infrastructure.Ingestion;
using Super.Infrastructure.Persistence;
using Super.Infrastructure.Sepa;

// Uso:
//   Super.Ingest migrate                         aplica las migraciones pendientes
//   Super.Ingest import <zip|carpeta>            importa Carrefour, Changomás y COTO del dump
//   Super.Ingest status                          últimas ingestas por cadena
//   Super.Ingest compare <ean[xcant]> ...        compara un carrito con los precios vigentes
// Conexión: variable SUPER_DB o ConnectionStrings:Super en appsettings.Local.json (carpeta actual o superiores).

var culture = CultureInfo.GetCultureInfo("es-AR");
CultureInfo.CurrentCulture = culture;
Console.OutputEncoding = Encoding.UTF8;   // la consola de Windows usa la página 850 por defecto: acentos rotos

if (args.Length == 0)
{
    Console.WriteLine("Uso: Super.Ingest migrate | import <zip|carpeta> | status | compare <ean[xcant]> ...");
    return 1;
}

var connectionString = ResolveConnectionString();
if (connectionString is null)
{
    Console.Error.WriteLine("Falta la conexión: definí SUPER_DB o creá appsettings.Local.json con ConnectionStrings:Super.");
    return 1;
}

await using var db = new SuperDbContext(SuperDbContext.CreateOptions(connectionString));

try
{
    switch (args[0])
    {
        case "migrate":
            await db.Database.MigrateAsync();
            Console.WriteLine("Migraciones aplicadas.");
            return 0;

        case "import" when args.Length == 2:
            return await ImportAsync(db, args[1]);

        case "status":
            await PrintStatusAsync(db);
            return 0;

        case "compare" when args.Length >= 2:
            return await CompareAsync(db, args[1..]);

        default:
            Console.Error.WriteLine($"Comando no reconocido: {string.Join(' ', args)}");
            return 1;
    }
}
catch (FileNotFoundException ex)
{
    Console.Error.WriteLine($"{ex.Message} ({ex.FileName})");
    return 1;
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine($"Dump inválido: {ex.Message}");
    return 1;
}
catch (Exception ex) when (ex is NpgsqlException || ex.InnerException is NpgsqlException)
{
    Console.Error.WriteLine($"No se pudo usar la base de datos: {(ex.InnerException ?? ex).Message}");
    return 2;
}

static async Task<int> ImportAsync(SuperDbContext db, string path)
{
    await db.Database.MigrateAsync();
    var files = SepaDump.Open(path);
    if (files.Count == 0)
    {
        Console.Error.WriteLine("No se encontraron archivos de Carrefour (10), Changomás (11) ni COTO (12).");
        return 1;
    }

    var service = new SepaIngestionService(db);
    foreach (var file in files)
    {
        using (file)
        {
            var sw = Stopwatch.StartNew();
            Console.Write($"{file.Chain.DisplayName(),-10} {file.Date:yyyy-MM-dd} ... ");
            var report = await file.ReadAsync((suc, prod) =>
                service.IngestAsync(file.Chain, file.Date, suc, prod, file.Name));
            Console.WriteLine(
                $"{report.Stats.Accepted:N0} precios, {report.Branches} sucursales, {report.DistinctProducts:N0} EAN " +
                $"({report.NewProducts:N0} nuevos){(report.ReplacedPreviousImport ? ", reemplazó la ingesta anterior" : "")} " +
                $"[{sw.Elapsed.TotalSeconds:N1} s]");
        }
    }
    return 0;
}

static async Task PrintStatusAsync(SuperDbContext db)
{
    var runs = await db.ImportRuns.OrderByDescending(r => r.PriceDate).ThenBy(r => r.Chain).Take(15).ToListAsync();
    if (runs.Count == 0) { Console.WriteLine("Todavía no hay ingestas."); return; }
    foreach (var r in runs)
        Console.WriteLine($"{r.PriceDate:yyyy-MM-dd} {r.Chain.DisplayName(),-10} {r.Accepted,8:N0} precios {r.DistinctProducts,7:N0} EAN {r.Branches,3} suc.  {r.ImportedAt.ToLocalTime():g}");
    Console.WriteLine($"Productos en catálogo: {await db.Products.CountAsync():N0}");
}

static async Task<int> CompareAsync(SuperDbContext db, string[] items)
{
    var cart = new List<CartLine>();
    foreach (var item in items)
    {
        var parts = item.Split('x', 2);
        var qty = parts.Length == 2 && int.TryParse(parts[1], out var q) ? q : 1;
        cart.Add(new CartLine(parts[0], qty));
    }

    var quotes = await new EfPriceQuoteSource(db).GetQuotesAsync(cart.Select(c => c.Ean));
    var result = new ComparisonEngine().Compare(cart, quotes);
    var names = await db.Products.AsNoTracking()
        .Where(p => quotes.Select(q => q.Ean).Contains(p.Ean))
        .ToDictionaryAsync(p => p.Ean, p => p.Description);

    foreach (var s in result.ChainSummaries)
        Console.WriteLine($"  {s.Chain.DisplayName(),-10} {s.CoveredItems}/{s.TotalItems} ítems  {(s.Total is { } t ? t.ToString("C") : "incompleto")}");
    Console.WriteLine($"  Repartido: {result.SplitTotal:C}  Brecha: {(result.Gap is { } g ? g.ToString("P1") : "—")}");
    Console.WriteLine(result.RecommendSingleStore
        ? $"→ Comprar todo en {result.BestSingleChain!.Value.DisplayName()}"
        : "→ Repartir la compra:");
    foreach (var (chain, lines) in result.Plan)
    {
        Console.WriteLine($"  [{chain.DisplayName()}]");
        foreach (var l in lines)
            Console.WriteLine($"    {l.Quantity} × {names.GetValueOrDefault(l.Ean, l.Ean)}  {l.UnitPrice:C}");
    }
    foreach (var e in result.UnavailableEans) Console.WriteLine($"  Sin precio en ninguna cadena: {e}");
    foreach (var e in result.InvalidCodes) Console.WriteLine($"  Código no comparable: {e}");
    return 0;
}

static string? ResolveConnectionString()
{
    var fromEnv = Environment.GetEnvironmentVariable("SUPER_DB");
    if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var file = Path.Combine(dir.FullName, "appsettings.Local.json");
        if (!File.Exists(file)) continue;
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
            && cs.TryGetProperty("Super", out var value))
            return value.GetString();
    }
    return null;
}
