using Super.Application.Catalog;
using Super.Application.Orders;

namespace Super.Tests;

public class CatalogSearchTests
{
    private static readonly CatalogSearch Catalog = new(
    [
        new CatalogEntry("7790360981042", "ACEITE GIRASOL X 900 ML", "NATURA", "900 ml"),
        new CatalogEntry("7790360981059", "ACEITE GIRASOL NATURA PET X 1.5 LT", "NATURA", "1,5 l"),
        new CatalogEntry("7792900000017", "AZÚCAR COMÚN X 1 KG", "LEDESMA", "1 kg"),
        new CatalogEntry("0000040084107", "HUEVO KINDER", "KINDER", "1 un"),
        new CatalogEntry("7791234500018", "LECHE 100% DESCREMADA", "SERENISIMA", "1 l"),
    ]);

    [Fact]
    public void AllWordsMustMatch_InDescriptionOrBrand()
    {
        var hits = Catalog.Search("girasol natura");
        Assert.Equal(["7790360981042", "7790360981059"], hits.Select(h => h.Ean));   // el más corto primero
    }

    [Theory]
    [InlineData("azucar")]
    [InlineData("AZÚCAR")]
    [InlineData("comun ledesma")]
    public void IgnoresCaseAndAccents(string query) =>
        Assert.Equal("7792900000017", Assert.Single(Catalog.Search(query)).Ean);

    [Fact]
    public void FindsByCodeAsTheCameraReadsIt()
    {
        Assert.Equal("0000040084107", Catalog.Find("40084107")!.Ean);
        Assert.Equal("0000040084107", Assert.Single(Catalog.Search("40084107")).Ean);
        Assert.Null(Catalog.Find("123"));
    }

    [Fact]
    public void PartialCode_FindsMatchingProducts() =>
        Assert.Equal(2, Catalog.Search("779036098").Count);

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("zzz no existe")]
    public void ShortOrUnknownQueries_ReturnNothing(string query) => Assert.Empty(Catalog.Search(query));

    [Fact]
    public void SpecialCharacters_AreLiteral() =>
        Assert.Equal("7791234500018", Assert.Single(Catalog.Search("100%")).Ean);

    [Fact]
    public void RespectsLimit() => Assert.Single(Catalog.Search("aceite", limit: 1));
}

public class OrderLinkTests
{
    [Fact]
    public void RoundTrip_KeepsNameItemsAndQuantities()
    {
        var link = OrderLink.Encode(new Order("Ana María", [new("7790360981042", 2), new("0000040084107", 1)]));
        Assert.Equal("pedido?de=Ana%20Mar%C3%ADa&i=7790360981042x2-40084107x1", link);

        var query = System.Web.HttpUtility.ParseQueryString(link.Split('?')[1]);
        var order = OrderLink.Decode(query["de"], query["i"])!;
        Assert.Equal("Ana María", order.From);
        Assert.Equal([new OrderItem("7790360981042", 2), new OrderItem("0000040084107", 1)], order.Items);
    }

    [Fact]
    public void Encode_MergesDuplicates_AndKeepsAtLeast8Digits()
    {
        var link = OrderLink.Encode(new Order("", [new("0000001234565", 1), new("1234565", 0), new("0000001234565", 2)]));
        Assert.Equal("pedido?i=01234565x3", link);
        Assert.Equal("0000001234565", OrderLink.Decode(null, "01234565x3")!.Items.Single().Ean);
    }

    [Fact]
    public void Decode_IgnoresInvalidParts_AndCapsQuantities()
    {
        var order = OrderLink.Decode("  Juan  ", "abc-7790360981042x5000-x2-40084107x0-40084107")!;
        Assert.Equal("Juan", order.From);
        Assert.Equal([new OrderItem("7790360981042", OrderLink.MaxQuantity), new OrderItem("0000040084107", 1)], order.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc-x3")]
    public void Decode_WithoutValidItems_ReturnsNull(string? items) => Assert.Null(OrderLink.Decode("Ana", items));

    [Fact]
    public void Decode_TrimsLongNamesAndControlCharacters()
    {
        var order = OrderLink.Decode(new string('a', 60) + "\n", "40084107")!;
        Assert.Equal(OrderLink.MaxNameLength, order.From.Length);
    }

    [Fact]
    public void Decode_LimitsTheNumberOfItems()
    {
        var many = string.Join("-", Enumerable.Range(0, 500).Select(i => $"{77900000 + i}"));
        Assert.Equal(OrderLink.MaxItems, OrderLink.Decode("", many)!.Items.Count);
    }
}
