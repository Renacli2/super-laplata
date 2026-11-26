using Super.Application.Catalog;
using Super.Web.Services;

namespace Super.Tests;

public class FavoritesStateTests
{
    private static readonly CatalogEntry Yerba = new("7790387000017", "YERBA MATE", "TARAGÜI", "1 kg");
    private static readonly CatalogEntry Azucar = new("7792540250450", "AZÚCAR", "LEDESMA", "1 kg");

    private static async Task<FavoritesState> NewFavoritesAsync(FakeStorage storage)
    {
        var favorites = new FavoritesState(storage);
        await favorites.LoadAsync();
        return favorites;
    }

    [Fact]
    public async Task Toggle_MarksAndUnmarks()
    {
        var favorites = await NewFavoritesAsync(new FakeStorage());

        Assert.True(await favorites.ToggleAsync(Yerba));
        Assert.True(favorites.IsFavorite(Yerba.Ean));

        Assert.False(await favorites.ToggleAsync(Yerba));
        Assert.False(favorites.IsFavorite(Yerba.Ean));
        Assert.Equal(0, favorites.Count);
    }

    [Fact]
    public async Task Favorites_SurviveReopeningTheApp_WithTheirQuantity()
    {
        var storage = new FakeStorage();
        var favorites = await NewFavoritesAsync(storage);
        await favorites.ToggleAsync(Yerba, quantity: 2);

        var reopened = await NewFavoritesAsync(storage);

        var item = Assert.Single(reopened.Items);
        Assert.Equal(Yerba.Ean, item.Ean);
        Assert.Equal("TARAGÜI", item.Brand);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public async Task IsFavorite_AcceptsTheCodeAsTheCameraReadsIt()
    {
        var kinder = new CatalogEntry("0000040084107", "KINDER", "KINDER", "1 un");
        var favorites = await NewFavoritesAsync(new FakeStorage());
        await favorites.ToggleAsync(kinder);

        Assert.True(favorites.IsFavorite("40084107"));
        Assert.Contains("0000040084107", favorites.Eans);
    }

    [Fact]
    public async Task Items_AreSortedByNameIgnoringAccents()
    {
        var favorites = await NewFavoritesAsync(new FakeStorage());
        await favorites.ToggleAsync(Yerba);
        await favorites.ToggleAsync(Azucar);

        Assert.Equal([Azucar.Ean, Yerba.Ean], favorites.Items.Select(f => f.Ean));
    }

    [Fact]
    public async Task Toggle_KeepsQuantityWithinOrderLimits()
    {
        var favorites = await NewFavoritesAsync(new FakeStorage());
        await favorites.ToggleAsync(Yerba, quantity: 0);

        Assert.Equal(1, Assert.Single(favorites.Items).Quantity);
    }

    [Fact]
    public async Task CorruptStorage_LoadsEmpty()
    {
        var storage = new FakeStorage();
        storage.Values["super.habituales.v1"] = "{no es json";

        var favorites = await NewFavoritesAsync(storage);

        Assert.Equal(0, favorites.Count);
    }
}
