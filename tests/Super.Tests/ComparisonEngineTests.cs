using Super.Application.Comparison;
using Super.Domain;

namespace Super.Tests;

public class ComparisonEngineTests
{
    private const string A = "7790000000011";
    private const string B = "7790000000028";

    private static PriceQuote Q(Chain chain, string ean, decimal price) => new(chain, ean, price);
    private static CartLine L(string ean, int qty = 1) => new(ean, qty);
    private readonly ComparisonEngine _engine = new();

    [Fact]
    public void DefaultThreshold_IsThreePercent() =>
        Assert.Equal(0.03m, ComparisonEngine.DefaultThreshold);

    [Fact]
    public void RecommendsSingleStore_WhenGapIsWithinThreshold()
    {
        var result = _engine.Compare(
            [L(A), L(B)],
            [Q(Chain.Carrefour, A, 100), Q(Chain.Carrefour, B, 100),
             Q(Chain.Coto, A, 99), Q(Chain.Coto, B, 102)]);

        Assert.Equal(199m, result.SplitTotal);
        Assert.Equal(Chain.Carrefour, result.BestSingleChain);
        Assert.Equal(200m, result.BestSingleTotal);
        Assert.True(result.RecommendSingleStore);
        Assert.Equal(Chain.Carrefour, Assert.Single(result.Plan).Key);
        Assert.Equal(2, result.Plan[Chain.Carrefour].Count);
    }

    [Fact]
    public void SplitsIntoOneListPerChain_WhenGapExceedsThreshold()
    {
        var result = _engine.Compare(
            [L(A), L(B)],
            [Q(Chain.Carrefour, A, 100), Q(Chain.Carrefour, B, 50),
             Q(Chain.Coto, A, 50), Q(Chain.Coto, B, 100)]);

        Assert.Equal(100m, result.SplitTotal);
        Assert.Equal(150m, result.BestSingleTotal);
        Assert.False(result.RecommendSingleStore);
        Assert.Equal(2, result.Plan.Count);
        Assert.Equal(B, result.Plan[Chain.Carrefour].Single().Ean);
        Assert.Equal(A, result.Plan[Chain.Coto].Single().Ean);
    }

    [Fact]
    public void GapExactlyAtThreshold_IsStillSingleStore()
    {
        // repartido = 100, mejor cadena única = 103 → brecha = 3 % exacto
        var result = _engine.Compare(
            [L(A), L(B)],
            [Q(Chain.Carrefour, A, 50), Q(Chain.Carrefour, B, 53),
             Q(Chain.Coto, A, 53), Q(Chain.Coto, B, 50)]);

        Assert.Equal(0.03m, result.Gap);
        Assert.True(result.RecommendSingleStore);
    }

    [Fact]
    public void CustomThreshold_ChangesTheDecision()
    {
        var quotes = new[]
        {
            Q(Chain.Carrefour, A, 100), Q(Chain.Carrefour, B, 100),
            Q(Chain.Coto, A, 90), Q(Chain.Coto, B, 110)
        };

        Assert.True(_engine.Compare([L(A), L(B)], quotes, threshold: 0.10m).RecommendSingleStore);
        Assert.False(_engine.Compare([L(A), L(B)], quotes, threshold: 0.01m).RecommendSingleStore);
    }

    [Fact]
    public void ChainMissingAnItem_CannotBeTheSingleStore()
    {
        var result = _engine.Compare(
            [L(A), L(B)],
            [Q(Chain.Carrefour, A, 100), Q(Chain.Carrefour, B, 100),
             Q(Chain.Coto, A, 10)]);                       // Coto no tiene B

        Assert.Equal(Chain.Carrefour, result.BestSingleChain);
        var coto = result.ChainSummaries.Single(s => s.Chain == Chain.Coto);
        Assert.False(coto.IsComplete);
        Assert.Equal(1, coto.CoveredItems);
        Assert.Equal(2, coto.TotalItems);
    }

    [Fact]
    public void NoChainHasEverything_ForcesSplit()
    {
        var result = _engine.Compare(
            [L(A), L(B)],
            [Q(Chain.Coto, A, 10), Q(Chain.Carrefour, B, 20)]);

        Assert.Null(result.BestSingleChain);
        Assert.Null(result.Gap);
        Assert.False(result.RecommendSingleStore);
        Assert.Equal(30m, result.SplitTotal);
        Assert.Equal(A, result.Plan[Chain.Coto].Single().Ean);
        Assert.Equal(B, result.Plan[Chain.Carrefour].Single().Ean);
    }

    [Fact]
    public void ItemsInNoChain_AreReportedAndExcludedFromTotals()
    {
        const string ghost = "7790000000035";
        var result = _engine.Compare(
            [L(A), L(ghost)],
            [Q(Chain.Carrefour, A, 100)]);

        Assert.Equal(new[] { ghost }, result.UnavailableEans);
        Assert.Equal(100m, result.SplitTotal);
        Assert.True(result.RecommendSingleStore);
    }

    [Fact]
    public void DuplicateLines_AreMerged_AndNormalizedByEan()
    {
        // Misma etiqueta: la cámara lee 40084107 y SEPA guarda 0000040084107.
        var result = _engine.Compare(
            [L("40084107", 1), L("0000040084107", 2)],
            [Q(Chain.Coto, "0000040084107", 10)]);

        var line = Assert.Single(result.Plan[Chain.Coto]);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(30m, result.SplitTotal);
    }

    [Fact]
    public void VariableWeightAndInvalidCodes_AreNotCompared()
    {
        var result = _engine.Compare(
            [L("2508398000001"), L("abc"), L(A)],
            [Q(Chain.Carrefour, A, 100)]);

        Assert.Equal(new[] { "2508398000001", "abc" }, result.InvalidCodes);
        Assert.Equal(100m, result.SplitTotal);
    }

    [Fact]
    public void KeepsCheapestPrice_WhenSameChainAndEanAppearTwice()
    {
        var result = _engine.Compare(
            [L(A)],
            [Q(Chain.Carrefour, A, 120), Q(Chain.Carrefour, A, 100)]);

        Assert.Equal(100m, result.SplitTotal);
    }

    [Fact]
    public void IgnoresNonPositivePricesAndQuantities()
    {
        var result = _engine.Compare(
            [L(A, 0), L(B, -2), L(B, 1)],
            [Q(Chain.Carrefour, B, 0), Q(Chain.Coto, B, 40)]);

        Assert.Equal(40m, result.SplitTotal);
        Assert.Equal(Chain.Coto, result.BestSingleChain);
    }

    [Fact]
    public void TiesBetweenChains_ResolveToLowerChainOrder()
    {
        var result = _engine.Compare(
            [L(A)],
            [Q(Chain.Coto, A, 100), Q(Chain.Changomas, A, 100)]);

        Assert.Equal(Chain.Changomas, result.BestSingleChain);
    }

    [Fact]
    public void EmptyCart_ReturnsEmptyResult()
    {
        var result = _engine.Compare([], [Q(Chain.Carrefour, A, 100)]);

        Assert.Equal(0m, result.SplitTotal);
        Assert.Null(result.BestSingleChain);
        Assert.False(result.RecommendSingleStore);
        Assert.Empty(result.Plan);
    }

    [Fact]
    public void NegativeThreshold_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _engine.Compare([L(A)], [], -0.01m));
}
