using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Super.Application.Comparison;
using Super.Domain;
using Super.Infrastructure.Ingestion;
using Super.Infrastructure.Persistence;

namespace Super.Tests;

public sealed class IngestionTests : PostgresTestBase
{
    private static readonly DateOnly Day1 = new(2026, 11, 13);
    private static readonly DateOnly Day2 = new(2026, 11, 14);

    private const string Sucursales =
        "﻿id_comercio|id_bandera|id_sucursal|sucursales_nombre|sucursales_calle|sucursales_numero|sucursales_latitud|sucursales_longitud|sucursales_localidad|sucursales_provincia\n" +
        "12|1|149|LA PLATA |Calle 1|1234|-34.92|-57.95|La Plata|AR-B\n" +
        "12|1|99|CITY BELL|Cantilo|500|-34.87|-58.05|La Plata Norte|AR-B\n" +
        "12|1|238|MAR DEL PLATA|Colón|1|-38.0|-57.5|Mar del Plata|AR-B\n";

    private const string Header =
        "﻿id_comercio|id_bandera|id_sucursal|id_producto|productos_ean|productos_descripcion|" +
        "productos_cantidad_presentacion|productos_unidad_medida_presentacion|productos_marca|" +
        "productos_precio_lista|productos_precio_referencia|productos_precio_unitario_promo1\n";

    private static string Productos(decimal aceite149, decimal aceite99) =>
        Header +
        $"12|1|149|7790360981042|1|ACEITE X 900 ML|900|ml|MARCA|{aceite149.ToString(CultureInfo.InvariantCulture)}|3889.44|\n" +
        $"12|1|99|7790360981042|1|ACEITE X 900 ML|900|ml|MARCA|{aceite99.ToString(CultureInfo.InvariantCulture)}|4000|3300\n" +
        "12|1|149|0000040084107|1|KINDER|1|uni|KINDER|2769.99|2769.99|0\n" +
        "12|1|238|7790360981042|1|ACEITE X 900 ML|900|ml|MARCA|1000|1111|\n";        // fuera de La Plata

    private async Task<IngestionReport> IngestAsync(Chain chain, DateOnly date, string productos)
    {
        await using var db = CreateContext();
        var sucursales = Sucursales.Replace("\n12|", $"\n{(int)chain + 9}|");
        return await new SepaIngestionService(db).IngestAsync(
            chain, date, new StringReader(sucursales), new StringReader(productos), "test.zip");
    }

    [SkippableFact]
    public async Task Ingest_SavesBranchesProductsAndChainMinimum()
    {
        var report = await IngestAsync(Chain.Coto, Day1, Productos(3500.50m, 3600m));

        Assert.Equal(2, report.Branches);
        Assert.Equal(2, report.DistinctProducts);
        Assert.Equal(2, report.NewProducts);
        Assert.False(report.ReplacedPreviousImport);

        await using var db = CreateContext();
        Assert.Equal(["99", "149"], await db.Branches.OrderBy(b => b.Name).Select(b => b.SepaBranchId).ToListAsync());

        var aceite = await db.ChainPrices.SingleAsync(p => p.Ean == "7790360981042");
        Assert.Equal(3500.50m, aceite.MinListPrice);
        Assert.Equal(2, aceite.BranchCount);

        Assert.Equal(3, await db.BranchPrices.CountAsync());
        var promo = await db.BranchPrices.Where(p => p.Ean == "7790360981042" && p.PromoPrice != null).SingleAsync();
        Assert.Equal(3300m, promo.PromoPrice);
        Assert.Null((await db.BranchPrices.SingleAsync(p => p.Ean == "0000040084107")).PromoPrice);  // promo 0 = sin promo

        var kinder = await db.Products.SingleAsync(p => p.Ean == "0000040084107");
        Assert.Equal("KINDER", kinder.Description);

        var run = await db.ImportRuns.SingleAsync();
        Assert.Equal(4, run.RowsRead);
        Assert.Equal(3, run.Accepted);
    }

    [SkippableFact]
    public async Task Reingest_SameChainAndDate_ReplacesPrices()
    {
        await IngestAsync(Chain.Coto, Day1, Productos(3500.50m, 3600m));
        var again = await IngestAsync(Chain.Coto, Day1, Productos(3700m, 3650m));

        Assert.True(again.ReplacedPreviousImport);
        Assert.Equal(0, again.NewProducts);

        await using var db = CreateContext();
        Assert.Equal(1, await db.ImportRuns.CountAsync());
        Assert.Equal(3, await db.BranchPrices.CountAsync());
        Assert.Equal(2, await db.Branches.CountAsync());
        Assert.Equal(3650m, (await db.ChainPrices.SingleAsync(p => p.Ean == "7790360981042")).MinListPrice);
    }

    [SkippableFact]
    public async Task Reingest_OneChain_DoesNotTouchTheOther()
    {
        await IngestAsync(Chain.Coto, Day1, Productos(3500m, 3600m));
        await IngestAsync(Chain.Carrefour, Day1, Productos(3400m, 3450m).Replace("\n12|", "\n10|"));
        await IngestAsync(Chain.Coto, Day1, Productos(3800m, 3900m));

        await using var db = CreateContext();
        var prices = await db.ChainPrices.Where(p => p.Ean == "7790360981042")
            .ToDictionaryAsync(p => p.Chain, p => p.MinListPrice);
        Assert.Equal(3400m, prices[Chain.Carrefour]);
        Assert.Equal(3800m, prices[Chain.Coto]);
        Assert.Equal(6, await db.BranchPrices.CountAsync());
    }

    [SkippableFact]
    public async Task PriceSource_UsesLatestDayOfEachChain_AndFeedsTheEngine()
    {
        await IngestAsync(Chain.Coto, Day1, Productos(3500m, 3600m));
        await IngestAsync(Chain.Coto, Day2, Productos(3900m, 3950m));
        await IngestAsync(Chain.Carrefour, Day1, Productos(3700m, 3800m).Replace("\n12|", "\n10|"));

        await using var db = CreateContext();
        var source = new EfPriceQuoteSource(db);

        // La cámara lee el EAN-8 sin ceros; "abc" se ignora.
        var quotes = await source.GetQuotesAsync(["7790360981042", "40084107", "abc"]);
        Assert.Contains(new PriceQuote(Chain.Coto, "7790360981042", 3900m), quotes);
        Assert.Contains(new PriceQuote(Chain.Carrefour, "7790360981042", 3700m), quotes);
        Assert.Equal(4, quotes.Count);

        var result = new ComparisonEngine().Compare(
            [new CartLine("7790360981042", 2), new CartLine("40084107", 1)], quotes);
        Assert.Equal(Chain.Carrefour, result.BestSingleChain);   // 2×3700 + 2769,99 < 2×3900 + 2769,99
        Assert.True(result.RecommendSingleStore);
    }

    [SkippableFact]
    public async Task PriceSource_WithNoImports_ReturnsNothing()
    {
        await using var db = CreateContext();
        Assert.Empty(await new EfPriceQuoteSource(db).GetQuotesAsync(["7790360981042"]));
    }
}
