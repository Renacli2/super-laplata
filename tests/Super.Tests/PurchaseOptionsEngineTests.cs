using Super.Application.Comparison;
using Super.Domain;

namespace Super.Tests;

public class PurchaseOptionsEngineTests
{
    private const string A = "7790000000011";
    private const string B = "7790000000028";
    private const string C = "7790000000035";

    private static PriceQuote Q(Chain chain, string ean, decimal price) => new(chain, ean, price);
    private static CartLine L(string ean, int qty = 1) => new(ean, qty);
    private readonly PurchaseOptionsEngine _engine = new();

    private static string Names(PurchaseOption o) => string.Join("+", o.Stores);

    [Fact]
    public void PairCompletesTheList_AtTheCheapestOfEachProduct()
    {
        // Carrefour barato en A, Changomás barato en B; COTO caro en todo.
        var r = _engine.Compute([L(A), L(B, 2)],
        [
            Q(Chain.Carrefour, A, 100), Q(Chain.Carrefour, B, 80),
            Q(Chain.Changomas, A, 130), Q(Chain.Changomas, B, 50),
            Q(Chain.Coto, A, 140), Q(Chain.Coto, B, 90),
        ]);

        var pair = r.Options.Single(o => Names(o) == "Carrefour+Changomas");
        Assert.Equal(200m, pair.Total);                                   // A 100 en Carrefour + 2×50 en Changomás
        Assert.Equal(A, pair.Plan[Chain.Carrefour].Single().Ean);
        Assert.Equal(B, pair.Plan[Chain.Changomas].Single().Ean);
        Assert.Same(pair, r.Cheapest);
        Assert.Same(pair, r.Recommended);                                 // 200 vs 230 (Changomás solo): ahorra 13 %
        Assert.Equal(Chain.Changomas, Assert.Single(r.BestSingleStore!.Stores));
    }

    [Fact]
    public void OptionsAreSortedByTotal_WithAllSinglesAndPairs()
    {
        var r = _engine.Compute([L(A), L(B)],
        [
            Q(Chain.Carrefour, A, 100), Q(Chain.Carrefour, B, 100),
            Q(Chain.Changomas, A, 90), Q(Chain.Changomas, B, 120),
            Q(Chain.Coto, A, 110), Q(Chain.Coto, B, 95),
        ]);

        Assert.All(r.Options, o => Assert.True(o.IsComplete));
        Assert.Equal(r.Options.Select(o => o.Total).Order(), r.Options.Select(o => o.Total));
        Assert.Contains(r.Options, o => Names(o) == "Changomas+Coto" && o.Total == 185m);
        Assert.Contains(r.Options, o => Names(o) == "Carrefour" && o.Total == 200m);
        Assert.DoesNotContain(r.Options, o => o.StoreCount == 3);         // con tres no mejora (Changomás+COTO ya es lo mínimo)
    }

    [Fact]
    public void ThreeStores_OnlyWhenCheaperThanAnyPair()
    {
        // Cada producto es más barato en una cadena distinta.
        var r = _engine.Compute([L(A), L(B), L(C)],
        [
            Q(Chain.Carrefour, A, 10), Q(Chain.Carrefour, B, 50), Q(Chain.Carrefour, C, 50),
            Q(Chain.Changomas, A, 50), Q(Chain.Changomas, B, 10), Q(Chain.Changomas, C, 50),
            Q(Chain.Coto, A, 50), Q(Chain.Coto, B, 50), Q(Chain.Coto, C, 10),
        ]);

        var three = Assert.Single(r.Options, o => o.StoreCount == 3);
        Assert.Equal(30m, three.Total);
        Assert.Same(three, r.Cheapest);
        Assert.Same(three, r.Recommended);                                // los pares cuestan 70: ahorro grande
    }

    [Fact]
    public void Recommendation_PrefersFewerStores_WithinThreshold()
    {
        // Repartir en dos ahorra 2 % (< 3 %): se recomienda un solo supermercado.
        var r = _engine.Compute([L(A), L(B)],
        [
            Q(Chain.Carrefour, A, 50), Q(Chain.Carrefour, B, 52),
            Q(Chain.Changomas, A, 52), Q(Chain.Changomas, B, 50),
        ]);

        Assert.Equal(100m, r.Cheapest!.Total);
        Assert.Equal(2, r.Cheapest.StoreCount);
        Assert.Equal(1, r.Recommended!.StoreCount);
        Assert.Equal(102m, r.Recommended.Total);
        Assert.Equal(0.02m, r.GapToCheapest(r.Recommended));
    }

    [Fact]
    public void CustomThreshold_ChangesTheRecommendation()
    {
        var quotes = new[]
        {
            Q(Chain.Carrefour, A, 50), Q(Chain.Carrefour, B, 52),
            Q(Chain.Changomas, A, 52), Q(Chain.Changomas, B, 50),
        };
        Assert.Equal(2, _engine.Compute([L(A), L(B)], quotes, threshold: 0.01m).Recommended!.StoreCount);
    }

    [Fact]
    public void PairEqualToASingleStore_IsNotRepeated()
    {
        // Carrefour es más barato en todo: el par Carrefour+COTO es la misma compra que Carrefour solo.
        var r = _engine.Compute([L(A), L(B)],
        [
            Q(Chain.Carrefour, A, 10), Q(Chain.Carrefour, B, 10),
            Q(Chain.Coto, A, 20), Q(Chain.Coto, B, 20),
        ]);

        Assert.Equal(["Carrefour", "Coto"], r.Options.Select(Names));
        Assert.Equal(1, r.Recommended!.StoreCount);
    }

    [Fact]
    public void IncompleteOptions_GoLast_WithWhatIsMissing()
    {
        // B solo lo vende COTO.
        var r = _engine.Compute([L(A), L(B)],
        [
            Q(Chain.Carrefour, A, 10),
            Q(Chain.Changomas, A, 12),
            Q(Chain.Coto, A, 15), Q(Chain.Coto, B, 30),
        ]);

        var complete = r.Options.TakeWhile(o => o.IsComplete).ToList();
        var incomplete = r.Options.SkipWhile(o => o.IsComplete).ToList();
        Assert.All(incomplete, o => Assert.False(o.IsComplete));
        Assert.Equal(r.Options.Count, complete.Count + incomplete.Count);

        Assert.Equal("Carrefour+Coto", Names(r.Cheapest!));               // A en Carrefour, B en COTO = 40
        Assert.Contains(incomplete, o => Names(o) == "Carrefour" && o.MissingEans.SequenceEqual([B]));
        Assert.Equal("Coto", Names(r.BestSingleStore!));                   // el único que tiene todo
    }

    [Fact]
    public void NoStoreHasEverything_ButAPairDoes()
    {
        var r = _engine.Compute([L(A), L(B)], [Q(Chain.Coto, A, 10), Q(Chain.Carrefour, B, 20)]);

        Assert.Null(r.BestSingleStore);
        Assert.Equal("Carrefour+Coto", Names(r.Recommended!));
        Assert.Equal(30m, r.Recommended!.Total);
    }

    [Fact]
    public void UnavailableAndInvalidCodes_StayOutOfEveryOption()
    {
        var r = _engine.Compute([L(A), L(C), L("2508398000001"), L("abc")], [Q(Chain.Carrefour, A, 100)]);

        Assert.Equal([C], r.UnavailableEans);
        Assert.Equal(["2508398000001", "abc"], r.InvalidCodes);
        Assert.All(r.Options, o => Assert.Empty(o.MissingEans));
        Assert.Equal(100m, r.Recommended!.Total);
    }

    [Fact]
    public void EmptyCart_HasNoOptions()
    {
        var r = _engine.Compute([], [Q(Chain.Carrefour, A, 100)]);
        Assert.Empty(r.Options);
        Assert.Null(r.Recommended);
        Assert.Null(r.Cheapest);
    }

    [Fact]
    public void SameEanTwiceInTheCart_IsMerged()
    {
        var r = _engine.Compute([L("40084107"), L("0000040084107", 2)], [Q(Chain.Coto, "0000040084107", 10)]);
        Assert.Equal(3, r.Recommended!.Plan[Chain.Coto].Single().Quantity);
    }
}
