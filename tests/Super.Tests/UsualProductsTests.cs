using Super.Web.Services;

namespace Super.Tests;

public class UsualProductsTests
{
    private const string Yerba = "7790387000017";
    private const string Azucar = "7792540250450";
    private const string Leche = "7791234500018";
    private const string Kinder = "0000040084107";

    private static readonly DateTimeOffset Day1 = new(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<string> NothingInList = [];

    private static Purchase Bought(int day, params (string Ean, string Description, int Quantity)[] items) =>
        new(Day1.AddDays(day), items.Select(i => new PurchasedItem(i.Ean, i.Description, i.Quantity, null, null, [])).ToList());

    private static FavoriteItem Star(string ean, string description, int quantity = 1) =>
        new() { Ean = ean, Description = description, Quantity = quantity };

    [Fact]
    public void BoughtTwice_IsSuggested_WithTheLastQuantity()
    {
        Purchase[] purchases =
        [
            Bought(0, (Yerba, "YERBA", 1)),
            Bought(7, (Yerba, "YERBA", 2), (Azucar, "AZUCAR", 1)),
        ];

        var usual = UsualProducts.Build([], purchases, NothingInList);

        var yerba = Assert.Single(usual);   // el azúcar se compró una sola vez: todavía no se sugiere
        Assert.Equal(Yerba, yerba.Ean);
        Assert.False(yerba.IsStarred);
        Assert.Equal(2, yerba.TimesBought);
        Assert.Equal(2, yerba.Quantity);
        Assert.Equal(Day1.AddDays(7), yerba.LastBought);
    }

    [Fact]
    public void Starred_GoFirst_ThenSuggestions_EachByTimesBought()
    {
        Purchase[] purchases =
        [
            Bought(0, (Leche, "LECHE", 1), (Yerba, "YERBA", 1), (Azucar, "AZUCAR", 1)),
            Bought(7, (Leche, "LECHE", 1), (Yerba, "YERBA", 1)),
            Bought(14, (Leche, "LECHE", 1)),
        ];
        FavoriteItem[] favorites = [Star(Kinder, "KINDER"), Star(Azucar, "AZUCAR")];

        var usual = UsualProducts.Build(favorites, purchases, NothingInList);

        // Marcados: azúcar (1 compra) antes que kinder (0). Sugeridos: leche (3) antes que yerba (2).
        Assert.Equal([Azucar, Kinder, Leche, Yerba], usual.Select(u => u.Ean));
        Assert.Equal([true, true, false, false], usual.Select(u => u.IsStarred));
    }

    [Fact]
    public void Starred_KeepsItsOwnQuantity_AndIsNotRepeatedAsSuggestion()
    {
        Purchase[] purchases = [Bought(0, (Yerba, "YERBA", 1)), Bought(7, (Yerba, "YERBA", 1))];

        var usual = UsualProducts.Build([Star(Yerba, "YERBA", quantity: 3)], purchases, NothingInList);

        var yerba = Assert.Single(usual);
        Assert.True(yerba.IsStarred);
        Assert.Equal(3, yerba.Quantity);
        Assert.Equal(2, yerba.TimesBought);
    }

    [Fact]
    public void ProductsAlreadyInTheList_AreLeftOut()
    {
        Purchase[] purchases = [Bought(0, (Yerba, "YERBA", 1)), Bought(7, (Yerba, "YERBA", 1))];

        var usual = UsualProducts.Build([Star(Azucar, "AZUCAR")], purchases, new HashSet<string> { Yerba, Azucar });

        Assert.Empty(usual);
    }

    [Fact]
    public void CodesAreCompared_AsTheCameraReadsThem()
    {
        Purchase[] purchases = [Bought(0, ("40084107", "KINDER", 1)), Bought(7, (Kinder, "KINDER", 1))];

        var usual = UsualProducts.Build([], purchases, NothingInList);

        var kinder = Assert.Single(usual);
        Assert.Equal(Kinder, kinder.Ean);
        Assert.Equal(2, kinder.TimesBought);
    }

    [Fact]
    public void PreferredEans_IncludeStarredAndSuggested_EvenIfInTheList()
    {
        Purchase[] purchases = [Bought(0, (Yerba, "YERBA", 1)), Bought(7, (Yerba, "YERBA", 1))];

        var preferred = UsualProducts.PreferredEans([Star(Azucar, "AZUCAR")], purchases);

        Assert.Equal([Yerba, Azucar], preferred.Order(StringComparer.Ordinal));
    }
}
