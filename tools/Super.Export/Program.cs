using System.Diagnostics;
using System.Globalization;
using System.Text;
using Super.Domain;
using Super.Infrastructure.Export;
using Super.Infrastructure.Sepa;

// Genera los archivos de datos de la app (version.json, catalog.json, prices.json). No usa base de datos.
// Uso:
//   Super.Export <zip|carpeta> <salida>        desde un dump ya descargado
//   Super.Export --download <salida> [día]     descarga el dump del día (hoy en Argentina, o lunes…domingo)

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Uso: Super.Export <zip|carpeta> <salida> | --download <salida> [lunes|martes|...]");
    return 1;
}

var sw = Stopwatch.StartNew();
string? downloaded = null;
try
{
    string source;
    var output = args[1];
    if (args[0] == "--download")
    {
        var day = args.Length == 3 ? ParseDay(args[2]) : SepaDownloader.TodayInArgentina();
        if (day is null)
        {
            Console.Error.WriteLine($"Día no reconocido: {args[2]}");
            return 1;
        }
        downloaded = Path.Combine(Path.GetTempPath(), $"sepa-{Guid.NewGuid():N}.zip");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        await new SepaDownloader(http).DownloadAsync(day.Value, downloaded, Console.WriteLine);
        source = downloaded;
    }
    else
    {
        source = args[0];
    }

    var files = SepaDump.Open(source);
    try
    {
        var report = await new StaticDataExporter().ExportAsync(files, output);
        foreach (var c in report.Chains)
            Console.WriteLine($"{c.Chain.DisplayName(),-10} {c.Date:yyyy-MM-dd}  {c.Stats.Accepted,7:N0} precios  {c.Eans,7:N0} EAN");
        Console.WriteLine($"Precios del {report.PriceDate:yyyy-MM-dd}: {report.Products:N0} productos. " +
                          $"catalog.json {report.CatalogBytes / 1024:N0} KB, prices.json {report.PricesBytes / 1024:N0} KB " +
                          $"[{sw.Elapsed.TotalSeconds:N0} s]");
    }
    finally
    {
        foreach (var f in files) f.Dispose();
    }
    return 0;
}
catch (Exception ex) when (ex is HttpRequestException or FileNotFoundException or InvalidDataException or IOException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
finally
{
    if (downloaded is not null && File.Exists(downloaded)) File.Delete(downloaded);
}

static DayOfWeek? ParseDay(string text) => text.ToLowerInvariant() switch
{
    "domingo" => DayOfWeek.Sunday,
    "lunes" => DayOfWeek.Monday,
    "martes" => DayOfWeek.Tuesday,
    "miercoles" or "miércoles" => DayOfWeek.Wednesday,
    "jueves" => DayOfWeek.Thursday,
    "viernes" => DayOfWeek.Friday,
    "sabado" or "sábado" => DayOfWeek.Saturday,
    _ => null,
};
