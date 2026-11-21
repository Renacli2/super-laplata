using Super.Application.Catalog;
using Super.Domain;

namespace Super.Tests;

public class EquivalentsTests
{
    private static CatalogEntry P(string ean, string description, string brand) => new(ean, description, brand, "1 uni");

    private static EquivalentFinder Finder(params (CatalogEntry Entry, decimal Price)[] products) =>
        new(products.Select(p => p.Entry),
            products.ToDictionary(p => p.Entry.Ean, p => new BestPrice(p.Price, [Chain.Coto])));

    [Theory]
    [InlineData("ACEITE GIRASOL LEGITIMO X 1.5L", 1500, SizeUnit.Milliliters)]
    [InlineData("DULCE DE LECHE FAMILIAR CARREFOUR CLASSIC POTX400G", 400, SizeUnit.Grams)]
    [InlineData("YERBA MATE C/PALO CACHAMAI PAQ 1 KGM", 1000, SizeUnit.Grams)]
    [InlineData("GASEOSA PRITTY CERO BOT 2,25 L", 2250, SizeUnit.Milliliters)]
    [InlineData("DETERGENTE BIOACTIVE LIMA CIF X 300 CC", 300, SizeUnit.Milliliters)]
    public void ParseSize_ReadsTheLastQuantityWithUnit(string description, int quantity, SizeUnit unit)
    {
        var size = ProductTraits.ParseSize(CatalogSearch.Fold(description));

        Assert.Equal(new ProductSize(quantity, unit), size);
    }

    [Theory]
    [InlineData("SF HUEVOS CASTA X1")]
    [InlineData("CERVEZA ROJA IMPERIAL LATA X 473")]
    public void ParseSize_WithoutUnit_IsUnknown(string description) =>
        Assert.Null(ProductTraits.ParseSize(CatalogSearch.Fold(description)));

    [Theory]
    [InlineData("LECHE ENTERA TREGAR TETRA X 1 LT", "TREGAR", "LECHE ENTERA")]
    [InlineData("CHECK ARROZ LARGO FINO 1KG", "CHECK", "ARROZ LARGO FINO")]
    [InlineData("GASEOSA COLA ZERO COCA COLA PET X 2.25 LT", "COCA COLA ZERO", "GASEOSA COLA ZERO")]
    [InlineData("YERBA MATE SUAVE UNION 4FLEX X 1KG", "UNION", "YERBA MATE SUAVE")]
    public void TypeWords_AreTheWordsBeforeTheBrand(string description, string brand, string type) =>
        Assert.Equal(type, string.Join(" ", ProductTraits.From(description, brand).TypeWords));

    [Fact]
    public void Find_SameTypeAndSize_CheaperPerLiter_IsSuggested()
    {
        var finder = Finder(
            (P("1", "LECHE ENTERA TREGAR TETRA X 1 LT", "TREGAR"), 2550),
            (P("2", "LECHE ENTERA UAT LA SUIPACHENSE X 1 LT", "LA SUIPACHENSE"), 2000),
            (P("3", "LECHE ENTERA SERENISIMA X 1 LT", "SERENISIMA"), 2600));

        var eq = Assert.Single(finder.Find("1"));
        Assert.Equal("2", eq.Product.Ean);
        Assert.Equal(2000m, eq.PerUnitPrice);
        Assert.Equal(1 - 2000m / 2550m, eq.Saving);
    }

    [Fact]
    public void Find_ComparesPerKilo_WithSlightlyDifferentSizes()
    {
        var finder = Finder(
            (P("1", "DULCE DE LECHE SER POTE X 400 GR", "SER"), 4700),
            (P("2", "DULCE DE LECHE FAMILIAR LA SUIPACHENSE POTE X360 G", "LA SUIPACHENSE"), 2900),
            (P("3", "DULCE DE LECHE FAMILIAR TONADITA X 1 KG", "TONADITA"), 5000));   // 1 kg: otro tamaño

        var eq = Assert.Single(finder.Find("1"));
        Assert.Equal("2", eq.Product.Ean);
    }

    [Fact]
    public void Find_VariantTheOriginalIsNot_IsNotSuggested()
    {
        var finder = Finder(
            (P("1", "DULCE DE LECHE SER POTE X 400 GR", "SER"), 4700),
            (P("2", "DULCE DE LECHE LIGHT CARREFOUR CLASSIC POTEX400GRS", "CARREFOUR CLASSIC"), 3090));

        Assert.Empty(finder.Find("1"));
    }

    [Fact]
    public void Find_ColaZero_IsNotReplacedByAnotherFlavour()
    {
        var finder = Finder(
            (P("1", "GASEOSA COLA ZERO COCA COLA PET X 2.25 LT", "COCA COLA ZERO"), 5749),
            (P("2", "GASEOSA PRITTY CERO BOT 2,25 L", "PRITTY CERO"), 2800),
            (P("3", "GASEOSA COLA ZERO PEPSI PET X 2.25 LT", "PEPSI"), 4000));

        var eq = Assert.Single(finder.Find("1"));
        Assert.Equal("3", eq.Product.Ean);
    }

    [Fact]
    public void Find_LessThanFivePercentCheaper_IsNotSuggested()
    {
        var finder = Finder(
            (P("1", "LAVANDINA ORIGINAL AYUDIN X 1 LT", "AYUDIN"), 1000),
            (P("2", "LAVANDINA ORIGINAL ODEX X 1 LT", "ODEX"), 970));

        Assert.Empty(finder.Find("1"));
    }

    [Fact]
    public void SavingFor_UsesTheBestEquivalentAndTheQuantity()
    {
        var finder = Finder(
            (P("1", "LECHE ENTERA TREGAR TETRA X 1 LT", "TREGAR"), 2550),
            (P("2", "LECHE ENTERA UAT LA SUIPACHENSE X 1 LT", "LA SUIPACHENSE"), 2000));

        Assert.Equal(2200m, finder.SavingFor("1", 4));   // (2550 − 2000) × 4
        Assert.Equal(0m, finder.SavingFor("2", 4));
    }
}
