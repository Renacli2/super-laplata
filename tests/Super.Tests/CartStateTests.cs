using Microsoft.JSInterop;
using Super.Application.Catalog;
using Super.Application.Comparison;
using Super.Application.Orders;
using Super.Domain;
using Super.Web.Services;

namespace Super.Tests;

public class CartStateTests
{
    private static readonly CatalogEntry Aceite = new("7790360981042", "ACEITE", "NATURA", "900 ml");
    private static readonly CatalogEntry Kinder = new("0000040084107", "KINDER", "KINDER", "1 un");
    private static readonly CatalogSearch Catalog = new([Aceite, Kinder]);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 18, 30, 0, TimeSpan.Zero);

    private static async Task<CartState> NewCartAsync(FakeStorage storage)
    {
        var cart = new CartState(storage, new FixedClock(Now));
        await cart.LoadAsync();
        return cart;
    }

    /// <summary>Líneas de la opción elegida, como las arma la comparación (por EAN normalizado).</summary>
    private static Dictionary<string, LineResult> Lines(params LineResult[] lines) =>
        lines.ToDictionary(l => Ean.Normalize(l.Ean)!, l => l with { Ean = Ean.Normalize(l.Ean)! });

    [Fact]
    public async Task InCart_SurvivesReopeningTheApp()
    {
        var storage = new FakeStorage();
        var cart = await NewCartAsync(storage);
        await cart.AddAsync(Aceite);
        await cart.AddAsync(Kinder);
        await cart.SetInCartAsync(Aceite.Ean, true);

        var reopened = await NewCartAsync(storage);
        Assert.True(reopened.Items.Single(i => i.Ean == Aceite.Ean).InCart);
        Assert.False(reopened.Items.Single(i => i.Ean == Kinder.Ean).InCart);
        Assert.Equal(1, reopened.InCartCount);
    }

    [Fact]
    public async Task AddingAProduct_KeepsTheOthersCheckedOff()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.SetInCartAsync(Aceite.Ean, true);

        await cart.AddAsync(Kinder);                    // se agrega algo nuevo y se vuelve a comparar

        Assert.True(cart.Items.Single(i => i.Ean == Aceite.Ean).InCart);
    }

    [Fact]
    public async Task AskingForMore_UnchecksTheProduct()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.AddAsync(Kinder);
        await cart.SetInCartAsync(Aceite.Ean, true);
        await cart.SetInCartAsync(Kinder.Ean, true);

        await cart.SetQuantityAsync(Aceite.Ean, 2);                                   // uno más a mano
        await cart.AddOrderAsync(new Order("Juan", [new OrderItem(Kinder.Ean, 1)]), Catalog);   // pedido nuevo

        Assert.All(cart.Items, i => Assert.False(i.InCart));
    }

    [Fact]
    public async Task AskingForLess_KeepsItCheckedOff()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.SetQuantityAsync(Aceite.Ean, 3);
        await cart.SetInCartAsync(Aceite.Ean, true);

        await cart.SetQuantityAsync(Aceite.Ean, 2);

        Assert.True(cart.Items.Single().InCart);
    }

    [Fact]
    public async Task ClearInCart_UnchecksEverything_AndKeepsTheList()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.AddAsync(Kinder);
        await cart.SetInCartAsync(Aceite.Ean, true);
        await cart.SetInCartAsync(Kinder.Ean, true);

        await cart.ClearInCartAsync();

        Assert.Equal(2, cart.Count);
        Assert.Equal(0, cart.InCartCount);
    }

    [Fact]
    public async Task ListsSavedBeforeThisChange_LoadAsNotCheckedOff()
    {
        var storage = new FakeStorage();
        storage.Values["super.lista.v2"] = """[{"Ean":"7790360981042","Description":"ACEITE","Quantity":2,"RequestedBy":["Ana"]}]""";

        var cart = await NewCartAsync(storage);

        var item = Assert.Single(cart.Items);
        Assert.Equal(2, item.Quantity);
        Assert.False(item.InCart);
    }

    [Fact]
    public async Task Replace_KeepsQuantityRequesterAndPosition()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddOrderAsync(new Order("Ana", [new OrderItem(Aceite.Ean, 3)]), Catalog);
        await cart.AddAsync(Kinder);   // queda primero
        var otroAceite = new CatalogEntry("7790000000011", "ACEITE GIRASOL", "OTRA", "900 ml");

        await cart.ReplaceAsync(Aceite.Ean, otroAceite);

        var item = cart.Items[1];
        Assert.Equal(otroAceite.Ean, item.Ean);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(["Ana"], item.RequestedBy);
        Assert.DoesNotContain(cart.Items, i => i.Ean == Aceite.Ean);
    }

    [Fact]
    public async Task Replace_WithAProductAlreadyInTheList_AddsTheQuantities()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.SetQuantityAsync(Aceite.Ean, 2);
        await cart.AddAsync(Kinder);

        await cart.ReplaceAsync(Aceite.Ean, Kinder);

        var item = Assert.Single(cart.Items);
        Assert.Equal(Kinder.Ean, item.Ean);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public async Task Add_WithQuantity_AddsThatMany()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);

        await cart.AddAsync(Aceite, quantity: 2);   // un habitual que se lleva de a 2

        Assert.Equal(3, Assert.Single(cart.Items).Quantity);
    }

    [Fact]
    public async Task AddMany_AddsEachWithItsQuantity_AndUnchecksWhatWasPicked()
    {
        var storage = new FakeStorage();
        var cart = await NewCartAsync(storage);
        await cart.AddAsync(Aceite);
        await cart.SetInCartAsync(Aceite.Ean, true);

        await cart.AddManyAsync([(Aceite, 2), (Kinder, 3)]);

        var reopened = await NewCartAsync(storage);
        Assert.Equal(3, reopened.Items.Single(i => i.Ean == Aceite.Ean).Quantity);
        Assert.False(reopened.Items.Single(i => i.Ean == Aceite.Ean).InCart);   // hay que llevar más
        Assert.Equal(3, reopened.Items.Single(i => i.Ean == Kinder.Ean).Quantity);
    }

    [Fact]
    public async Task ConfirmPurchase_RemovesCheckedItems_AndKeepsTheRestUnchecked()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.AddAsync(Kinder);
        await cart.SetInCartAsync(Aceite.Ean, true);

        var purchase = await cart.ConfirmPurchaseAsync(Lines());

        Assert.NotNull(purchase);
        var left = Assert.Single(cart.Items);
        Assert.Equal(Kinder.Ean, left.Ean);
        Assert.False(left.InCart);
    }

    [Fact]
    public async Task ConfirmPurchase_WithNothingChecked_ReturnsNullAndChangesNothing()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);

        var purchase = await cart.ConfirmPurchaseAsync(Lines());

        Assert.Null(purchase);
        Assert.Equal(1, cart.Count);
        Assert.Empty(cart.Purchases);
        Assert.False(cart.CanUndoPurchase);
    }

    [Fact]
    public async Task ConfirmPurchase_RecordsQuantityChainPriceAndRequester()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddOrderAsync(new Order("Ana", [new OrderItem(Aceite.Ean, 2), new OrderItem(Kinder.Ean, 1)]), Catalog);
        await cart.SetInCartAsync(Aceite.Ean, true);
        await cart.SetInCartAsync(Kinder.Ean, true);

        var purchase = await cart.ConfirmPurchaseAsync(Lines(new LineResult(Aceite.Ean, 2, Chain.Coto, 2500m)));

        Assert.NotNull(purchase);
        Assert.Equal(Now, purchase.At);
        var aceite = purchase.Items.Single(i => i.Ean == Aceite.Ean);
        Assert.Equal(2, aceite.Quantity);
        Assert.Equal(nameof(Chain.Coto), aceite.Chain);
        Assert.Equal(5000m, aceite.Total);
        Assert.Equal(["Ana"], aceite.RequestedBy);
        var kinder = purchase.Items.Single(i => i.Ean == Kinder.Ean);   // sin precio en la opción
        Assert.Null(kinder.Chain);
        Assert.Null(kinder.Total);
        Assert.Equal(5000m, purchase.EstimatedTotal);
        Assert.Equal(0, cart.Count);
    }

    [Fact]
    public async Task ConfirmPurchase_SurvivesReopeningTheApp()
    {
        var storage = new FakeStorage();
        var cart = await NewCartAsync(storage);
        await cart.AddAsync(Aceite);
        await cart.AddAsync(Kinder);
        await cart.SetInCartAsync(Aceite.Ean, true);
        await cart.ConfirmPurchaseAsync(Lines(new LineResult(Aceite.Ean, 1, Chain.Carrefour, 2500m)));

        var reopened = await NewCartAsync(storage);

        Assert.Equal(Kinder.Ean, Assert.Single(reopened.Items).Ean);
        var purchase = Assert.Single(reopened.Purchases);
        Assert.Equal(2500m, Assert.Single(purchase.Items).Total);
        Assert.False(reopened.CanUndoPurchase);   // el deshacer no sobrevive al cerrar la app
    }

    [Fact]
    public async Task UndoPurchase_RestoresTheListCheckedOff_AndRemovesTheRecord()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.AddAsync(Kinder);
        await cart.SetInCartAsync(Aceite.Ean, true);
        var before = cart.Items.Select(i => i.Ean).ToList();
        await cart.ConfirmPurchaseAsync(Lines());

        var undone = await cart.UndoPurchaseAsync();

        Assert.True(undone);
        Assert.Equal(before, cart.Items.Select(i => i.Ean));
        Assert.True(cart.Items.Single(i => i.Ean == Aceite.Ean).InCart);
        Assert.Empty(cart.Purchases);
        Assert.False(cart.CanUndoPurchase);
    }

    [Fact]
    public async Task UndoPurchase_AfterChangingTheList_IsNotAllowed()
    {
        var cart = await NewCartAsync(new FakeStorage());
        await cart.AddAsync(Aceite);
        await cart.AddAsync(Kinder);
        await cart.SetInCartAsync(Aceite.Ean, true);
        await cart.ConfirmPurchaseAsync(Lines());

        await cart.SetQuantityAsync(Kinder.Ean, 2);   // se tocó la lista después de confirmar

        Assert.False(cart.CanUndoPurchase);
        Assert.False(await cart.UndoPurchaseAsync());
        Assert.Equal(Kinder.Ean, Assert.Single(cart.Items).Ean);
        Assert.Single(cart.Purchases);
    }

    [Fact]
    public async Task ConfirmPurchase_KeepsOnlyTheLatestPurchases()
    {
        var cart = await NewCartAsync(new FakeStorage());
        for (var n = 0; n < CartState.MaxPurchases + 3; n++)
        {
            await cart.AddAsync(Aceite);
            await cart.SetInCartAsync(Aceite.Ean, true);
            await cart.ConfirmPurchaseAsync(Lines());
        }

        Assert.Equal(CartState.MaxPurchases, cart.Purchases.Count);
    }
}
