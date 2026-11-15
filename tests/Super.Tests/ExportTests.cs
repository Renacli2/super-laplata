using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Super.Contracts;
using Super.Infrastructure.Export;
using Super.Infrastructure.Sepa;

namespace Super.Tests;

public sealed class ExportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("super-export-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Header =
        "id_comercio|id_bandera|id_sucursal|id_producto|productos_ean|productos_descripcion|" +
        "productos_cantidad_presentacion|productos_unidad_medida_presentacion|productos_marca|" +
        "productos_precio_lista|productos_precio_referencia|productos_precio_unitario_promo1\n";

    private const string Branches =
        "id_comercio|id_bandera|id_sucursal|sucursales_nombre|sucursales_localidad|sucursales_provincia\n" +
        "1|1|1|LA PLATA|La Plata|AR-B\n" +
        "1|1|2|CITY BELL|La Plata Norte|AR-B\n" +
        "1|1|3|MDQ|Mar del Plata|AR-B\n";

    private void Zip(string name, string productos)
    {
        using var zip = ZipFile.Open(Path.Combine(_dir, name), ZipArchiveMode.Create);
        foreach (var (entry, content) in new[] { ("sucursales.csv", Branches), ("productos.csv", productos) })
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
    }

    [Fact]
    public async Task Export_WritesCatalogPricesAndVersion()
    {
        Zip("sepa_1_comercio-sepa-10_2026-11-13_01-05-07.zip", Header +
            "10|1|1|7790360981042|1|ACEITE X 900 ML|900|ml|NATURA|3500|3888|\n" +
            "10|1|2|7790360981042|1|ACEITE X 900 ML|900|ml|NATURA|3400|3777|\n" +       // más barato en otra sucursal
            "10|1|3|7790360981042|1|ACEITE X 900 ML|900|ml|NATURA|1000|1111|\n" +       // fuera de La Plata
            "10|1|1|40084107|1|KINDER|1|uni|KINDER|2800|2800|\n");
        Zip("sepa_1_comercio-sepa-12_2026-11-13_09-05-10.zip", Header +
            "12|1|1|7790360981042|1|ACEITE GIRASOL NATURA 900|0.9|LT|NATURA|3300|3666|\n" +
            "12|1|1|2004015600000|1|ACELGA|1|uni||1499|1499|\n");                        // código interno: no entra

        var files = SepaDump.Open(_dir);
        var out_ = Path.Combine(_dir, "data");
        ExportReport report;
        try { report = await new StaticDataExporter().ExportAsync(files, out_); }
        finally { foreach (var f in files) f.Dispose(); }

        Assert.Equal(new DateOnly(2026, 11, 13), report.PriceDate);
        Assert.Equal(2, report.Products);

        var catalog = Read<CatalogFileDto>("catalog.json");
        Assert.Equal(["0000040084107", "7790360981042"], catalog.Products.Select(p => p.Ean));
        var aceite = catalog.Products.Single(p => p.Ean == "7790360981042");
        Assert.Equal("ACEITE X 900 ML", aceite.Description);      // datos de la primera cadena (Carrefour)
        Assert.Equal("900 ml", aceite.Presentation);

        var prices = Read<PricesFileDto>("prices.json");
        Assert.Equal(["Carrefour", "Coto"], prices.Chains.Select(c => c.Id));
        var row = prices.Prices.Single(p => p.Ean == "7790360981042");
        Assert.Equal([3400m, 3300m], row.Prices);
        Assert.Equal([2800m, null], prices.Prices.Single(p => p.Ean == "0000040084107").Prices);

        var version = Read<DataVersionDto>("version.json");
        Assert.Equal(2, version.Products);

        var raw = File.ReadAllText(Path.Combine(out_, "catalog.json"));
        Assert.Contains("\"e\":\"7790360981042\"", raw);           // nombres cortos

        T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(out_, file)))!;
    }

    [Fact]
    public async Task Export_WithoutChainFiles_Fails() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => new StaticDataExporter().ExportAsync([], _dir));

    [Theory]
    [InlineData(900.0, "ml", "900 ml")]
    [InlineData(0.8, "KG", "0,8 kg")]
    [InlineData(null, "kg", "")]
    [InlineData(1.0, " ", "")]
    public void Presentation_IsReadable(double? qty, string unit, string expected) =>
        Assert.Equal(expected, StaticDataExporter.Presentation((decimal?)qty, unit));
}

public class SepaDownloaderTests
{
    [Fact]
    public void UrlFor_UsesTheResourceOfEachWeekday()
    {
        Assert.EndsWith("/91bc072a-4726-44a1-85ec-4a8467aad27e/download/sepa_viernes.zip", SepaDownloader.UrlFor(DayOfWeek.Friday).ToString());
        Assert.EndsWith("/download/sepa_domingo.zip", SepaDownloader.UrlFor(DayOfWeek.Sunday).ToString());
        Assert.Equal(7, Enum.GetValues<DayOfWeek>().Select(SepaDownloader.UrlFor).Distinct().Count());
    }

    [Theory]
    [InlineData("2026-11-14T02:30:00Z", DayOfWeek.Friday)]     // 23:30 del viernes en Argentina
    [InlineData("2026-11-14T03:30:00Z", DayOfWeek.Saturday)]
    public void TodayInArgentina_UsesUtcMinus3(string utc, DayOfWeek expected) =>
        Assert.Equal(expected, SepaDownloader.TodayInArgentina(new FixedClock(DateTimeOffset.Parse(utc))));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
