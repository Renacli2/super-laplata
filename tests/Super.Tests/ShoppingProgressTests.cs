using Super.Application.Comparison;
using Super.Domain;
using Super.Web.Services;

namespace Super.Tests;

public class ShoppingProgressTests
{
    private const string A = "7790000000011";
    private const string B = "7790000000028";
    private const string C = "7790000000035";

    private static LineResult Line(string ean, int qty, Chain chain, decimal price) => new(ean, qty, chain, price);

    [Fact]
    public void For_NothingPicked_AllRemains()
    {
        var p = ShoppingProgress.For([Line(A, 2, Chain.Coto, 100), Line(B, 1, Chain.Coto, 50)], new HashSet<string>());

        Assert.Equal(0m, p.PickedTotal);
        Assert.Equal(250m, p.RemainingTotal);
        Assert.Equal(2, p.RemainingCount);
        Assert.False(p.IsDone);
    }

    [Fact]
    public void For_SomePicked_SplitsMoneyAndProducts()
    {
        var p = ShoppingProgress.For([Line(A, 2, Chain.Coto, 100), Line(B, 1, Chain.Coto, 50)], new HashSet<string> { A });

        Assert.Equal(200m, p.PickedTotal);
        Assert.Equal(50m, p.RemainingTotal);
        Assert.Equal(1, p.PickedCount);
        Assert.Equal(1, p.RemainingCount);
        Assert.Equal(250m, p.Total);
    }

    [Fact]
    public void For_Option_CountsTheComplementToo()
    {
        // Changomás no vende C: se completa en Carrefour y también cuenta para lo que falta.
        var r = new PurchaseOptionsEngine().Compute([new CartLine(A, 1), new CartLine(B, 1), new CartLine(C, 1)],
        [
            new PriceQuote(Chain.Changomas, A, 100), new PriceQuote(Chain.Changomas, B, 50),
            new PriceQuote(Chain.Carrefour, A, 300), new PriceQuote(Chain.Carrefour, B, 40), new PriceQuote(Chain.Carrefour, C, 40),
        ]);
        var changomas = r.Options.Single(o => o.Stores.SequenceEqual([Chain.Changomas]));

        var p = ShoppingProgress.For(changomas, new HashSet<string> { A });

        Assert.Equal(100m, p.PickedTotal);
        Assert.Equal(90m, p.RemainingTotal);   // B 50 en Changomás + C 40 en Carrefour
        Assert.Equal(2, p.RemainingCount);
    }

    [Fact]
    public void For_EverythingPicked_IsDone()
    {
        var p = ShoppingProgress.For([Line(A, 1, Chain.Coto, 10)], new HashSet<string> { A });

        Assert.True(p.IsDone);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void IsStale_AfterTwoDays_IsTrue(int days, bool stale)
    {
        var today = new DateOnly(2026, 11, 21);

        Assert.Equal(stale, Freshness.IsStale(today.AddDays(-days), today));
    }

    [Theory]
    [InlineData(0, "hoy")]
    [InlineData(1, "ayer")]
    [InlineData(4, "hace 4 días")]
    public void Ago_DescribesTheAge(int days, string text) => Assert.Equal(text, Freshness.Ago(days));
}
