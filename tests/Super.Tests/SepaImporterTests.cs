using Super.Domain;
using Super.Infrastructure.Sepa;

namespace Super.Tests;

public class SepaImporterTests
{
    // Encabezado con BOM, como en los archivos reales de SEPA.
    private const string Sucursales =
        "﻿id_comercio|id_bandera|id_sucursal|sucursales_nombre|sucursales_localidad|sucursales_provincia\n" +
        "12|1|149|LA PLATA |La Plata|AR-B\n" +
        "12|1|238|MAR DEL PLATA|Mar del Plata|AR-B\n" +
        "12|1|6|AV. LA PLATA|Pompeya|AR-C\n" +
        "12|1|99|OTRA|LA PLATA NOROESTE DE LA CALLE 50|AR-B\n";

    private const string Productos =
        "﻿id_comercio|id_bandera|id_sucursal|id_producto|productos_ean|productos_descripcion|" +
        "productos_cantidad_presentacion|productos_unidad_medida_presentacion|productos_marca|" +
        "productos_precio_lista|productos_precio_referencia|productos_precio_unitario_promo1\n" +
        "12|1|149|7790360981042|1|ACEITE X 900 ML|900|ml|MARCA|3500.50|3889.44|\n" +   // aceptada
        "12|1|149|2004015600000|0|ACELGA PAQ 1 UNI|1|uni| |1499|1499|\n" +             // sin EAN
        "12|1|149|0000040084107|1|KINDER|1|uni|KINDER|2769.99|2769.99|2500\n" +        // aceptada, con promo
        "12|1|238|7790360981042|1|ACEITE X 900 ML|900|ml|MARCA|3000|3333.33|\n" +      // otra zona
        "12|1|149|7791111111119|1|BARATO|1|uni|X|0.10|0.10|\n" +                       // precio fuera de rango
        "12|1|149|abc|1|ROTO|1|uni|X|100|100|\n" +                                      // EAN inválido
        "12|1|99|7790360981042|1|ACEITE X 900 ML|900|ml|MARCA|3600|4000|\n";            // otra sucursal de La Plata

    [Theory]
    [InlineData("La Plata", "AR-B", true)]
    [InlineData("LA PLATA NOROESTE DE LA CALLE 50", "AR-B", true)]
    [InlineData("Mar del Plata", "AR-B", false)]
    [InlineData("La Plata", "AR-C", false)]
    [InlineData("Pompeya", "AR-B", false)]
    public void BranchFilter_MatchesOnlyLaPlata(string locality, string province, bool expected)
    {
        var csv = $"id_bandera|id_sucursal|sucursales_localidad|sucursales_provincia\n1|1|{locality}|{province}\n";
        var row = SepaCsv.Read(new StringReader(csv)).Single();

        Assert.Equal(expected, new LaPlataBranchFilter().IsMatch(row));
    }

    [Fact]
    public void Import_CountsAndFiltersAsExpected()
    {
        var result = new SepaImporter().Import(
            Chain.Coto, new StringReader(Sucursales), new StringReader(Productos));

        var s = result.Stats;
        Assert.Equal(7, s.RowsRead);
        Assert.Equal(6, s.RowsInZone);       // todas menos la de Mar del Plata
        Assert.Equal(1, s.SkippedNoEan);
        Assert.Equal(1, s.SkippedInvalidEan);
        Assert.Equal(1, s.SkippedBadPrice);
        Assert.Equal(3, s.Accepted);
        Assert.Equal(3, result.Prices.Count);
    }

    [Fact]
    public void Import_UsesIdProductoAsEan_AndNormalizesIt()
    {
        var result = new SepaImporter().Import(
            Chain.Coto, new StringReader(Sucursales), new StringReader(Productos));

        Assert.Contains(result.Prices, p => p.Ean == "0000040084107" && p.PromoPrice == 2500m);
        Assert.Contains(result.Prices, p => p.Ean == "7790360981042" && p.ListPrice == 3500.50m);
    }

    [Fact]
    public void CheapestPerChain_KeepsLowestPriceAcrossBranches()
    {
        var result = new SepaImporter().Import(
            Chain.Coto, new StringReader(Sucursales), new StringReader(Productos));

        var quote = SepaImporter.CheapestPerChain(result.Prices)
            .Single(q => q.Ean == "7790360981042");

        Assert.Equal(Chain.Coto, quote.Chain);
        Assert.Equal(3500.50m, quote.UnitPrice);   // sucursal 149 (3500,50) vs. sucursal 99 (3600)
    }

    [Fact]
    public void Csv_SkipsMalformedAndEmptyLines()
    {
        var csv = "a|b|c\n1|2|3\n\n4|5\n6|7|8\n";
        var rows = SepaCsv.Read(new StringReader(csv)).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("6", rows[1]["a"]);
    }

    [Theory]
    [InlineData("10", Chain.Carrefour)]
    [InlineData("11", Chain.Changomas)]
    [InlineData("12", Chain.Coto)]
    public void SepaCommerceIds_MapToChains(string id, Chain expected)
    {
        Assert.True(ChainExtensions.TryFromSepaCommerceId(id, out var chain));
        Assert.Equal(expected, chain);
    }

    [Fact]
    public void UnknownCommerceId_IsRejected() =>
        Assert.False(ChainExtensions.TryFromSepaCommerceId("15", out _));
}
