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
    public void IncompleteOptions_AreSortedByTheWholeList_WithTheirComplement()
    {
        // C solo lo vende Changomás. COTO solo no lo tiene: se completa en Changomás.
        var r = _engine.Compute([L(A), L(B), L(C)],
        [
            Q(Chain.Carrefour, A, 10), Q(Chain.Carrefour, B, 10),
            Q(Chain.Changomas, A, 20), Q(Chain.Changomas, B, 20), Q(Chain.Changomas, C, 5),
            Q(Chain.Coto, A, 9), Q(Chain.Coto, B, 30),
        ]);

        var coto = r.Options.Single(o => Names(o) == "Coto");
        Assert.False(coto.IsComplete);
        Assert.Equal([C], coto.MissingEans);
        Assert.Equal(39m, coto.Total);                                    // A 9 + B 30 en COTO
        Assert.Equal(C, coto.Complement[Chain.Changomas].Single().Ean);
        Assert.Equal(44m, coto.FullTotal);                                // + C 5 en Changomás
        Assert.Equal(5m / 44m, coto.MissingShare);

        // Un solo orden por lista entera: triple 24, Carrefour+Changomás 25, Changomás+COTO 34, COTO 44, Changomás 45.
        Assert.Equal([24m, 25m, 34m, 44m, 45m], r.Options.Select(o => o.FullTotal));
        Assert.Equal(3, r.Options[0].StoreCount);
        Assert.Equal(20m / 24m, r.GapToCheapest(coto));
    }

    [Fact]
    public void IncompleteOptionEqualToAPair_IsShownOnceAsThePair()
    {
        // Carrefour solo + B completado en COTO es exactamente la compra Carrefour + COTO.
        var r = _engine.Compute([L(A), L(B)],
        [
            Q(Chain.Carrefour, A, 10),
            Q(Chain.Changomas, A, 12),
            Q(Chain.Coto, A, 15), Q(Chain.Coto, B, 30),
        ]);

        Assert.DoesNotContain(r.Options, o => Names(o) == "Carrefour");
        var pair = Assert.Single(r.Options, o => Names(o) == "Carrefour+Coto");
        Assert.True(pair.IsComplete);
        Assert.Same(pair, r.Cheapest);
        Assert.Equal("Coto", Names(r.BestSingleStore!));
    }

    [Fact]
    public void SmallSecondStore_IsAShortStop_AndWeighsHalf()
    {
        // X: COTO 100, Changomás 104. Y (chico) solo en Changomás a 4: COTO + parada corta en Changomás = 104.
        // Changomás solo = 108 (3,8 % más): no entra en el 3 %, se recomienda COTO + parada corta.
        var r = _engine.Compute([L(A), L(B)],
        [
            Q(Chain.Coto, A, 100),
            Q(Chain.Changomas, A, 104), Q(Chain.Changomas, B, 4),
        ]);

        var rec = r.Recommended!;
        Assert.True(rec.IsComplete);                                      // es el par, con Changomás chiquito
        Assert.Equal([Chain.Coto], rec.MainStores);
        Assert.Equal([Chain.Changomas], rec.ShortStops);
        Assert.Equal(1.5m, rec.Effort);
        Assert.Equal(104m, rec.FullTotal);
    }

    [Fact]
    public void SingleStoreWithinThreshold_BeatsAShortStop()
    {
        var r = _engine.Compute([L(A), L(B)],
        [
            Q(Chain.Coto, A, 100),
            Q(Chain.Changomas, A, 101), Q(Chain.Changomas, B, 4),            // Changomás solo = 105 (< 3 % sobre 104)
        ]);

        Assert.Equal(1m, r.Recommended!.Effort);
        Assert.Equal("Changomas", Names(r.Recommended));
    }

    [Fact]
    public void MainStorePlusShortStopForWhatIsMissing_CanBeRecommended()
    {
        // COTO tiene A y B; C (3) solo en Changomás. COTO + C en Changomás = 203, falta 1,5 %: parada corta.
        // El reparto óptimo COTO + Changomás = 201 (B más barato en Changomás) obliga a dos compras grandes.
        var r = _engine.Compute([L(A), L(B), L(C)],
        [
            Q(Chain.Coto, A, 100), Q(Chain.Coto, B, 100),
            Q(Chain.Changomas, A, 120), Q(Chain.Changomas, B, 98), Q(Chain.Changomas, C, 3),
        ]);

        Assert.Equal(201m, r.Cheapest!.FullTotal);
        Assert.Equal(2m, r.Cheapest.Effort);
        var rec = r.Recommended!;
        Assert.False(rec.IsComplete);
        Assert.Equal("Coto", Names(rec));
        Assert.Equal(203m, rec.FullTotal);
        Assert.Equal([Chain.Changomas], rec.ShortStops);
        Assert.True(r.IsEligible(rec));
    }

    [Fact]
    public void OptionMissingMoreThanTheShortStopShare_IsNeverRecommended()
    {
        // COTO solo cuesta casi lo mismo, pero lo que falta (C) es el 20 % de la compra.
        var r = _engine.Compute([L(A), L(C)],
        [
            Q(Chain.Coto, A, 80),
            Q(Chain.Carrefour, A, 81), Q(Chain.Carrefour, C, 20),
        ]);

        var coto = r.Options.FirstOrDefault(o => Names(o) == "Coto");
        if (coto is not null) Assert.False(r.IsEligible(coto));
        Assert.True(r.Recommended!.IsComplete);
        Assert.Equal("Carrefour", Names(r.Recommended));                  // 101 en un solo lugar
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
