using Microsoft.JSInterop;
using Super.Application.Catalog;
using Super.Application.Orders;
using Super.Web.Services;

namespace Super.Tests;

public class CartStateTests
{
    private static readonly CatalogEntry Aceite = new("7790360981042", "ACEITE", "NATURA", "900 ml");
    private static readonly CatalogEntry Kinder = new("0000040084107", "KINDER", "KINDER", "1 un");
    private static readonly CatalogSearch Catalog = new([Aceite, Kinder]);

    /// <summary>localStorage de mentira, compartido entre instancias (como reabrir la app).</summary>
    private sealed class FakeStorage : IJSRuntime
    {
        public Dictionary<string, string> Values { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "superApp.storageGet":
                    return ValueTask.FromResult((TValue)(object?)Values.GetValueOrDefault((string)args![0]!)!);
                case "superApp.storageSet":
                    Values[(string)args![0]!] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                default:
                    throw new InvalidOperationException(identifier);
            }
        }
    }

    private static async Task<CartState> NewCartAsync(FakeStorage storage)
    {
        var cart = new CartState(storage);
        await cart.LoadAsync();
        return cart;
    }

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
}
