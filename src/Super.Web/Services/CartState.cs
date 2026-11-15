using System.Text.Json;
using Microsoft.JSInterop;
using Super.Contracts;

namespace Super.Web.Services;

public sealed class CartItem
{
    public required string Ean { get; init; }
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Presentation { get; set; } = "";
    public decimal? BestPrice { get; set; }
    public string? BestChainName { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// La lista de compras, guardada en el navegador (localStorage) para que sobreviva al cerrar la app.
/// Si el navegador no deja guardar (modo privado), la lista funciona igual mientras la app esté abierta.
/// </summary>
public sealed class CartState(IJSRuntime js)
{
    private const string StorageKey = "super.lista.v1";
    private const int MaxQuantity = 999;
    private List<CartItem> _items = [];
    private bool _loaded;

    public event Action? Changed;

    public IReadOnlyList<CartItem> Items => _items;
    public int Count => _items.Count;
    public int Units => _items.Sum(i => i.Quantity);

    /// <summary>Suma de los mejores precios por producto (lo mínimo que se puede gastar).</summary>
    public decimal EstimatedTotal => _items.Sum(i => (i.BestPrice ?? 0m) * i.Quantity);

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await js.InvokeAsync<string?>("superApp.storageGet", StorageKey);
            if (!string.IsNullOrEmpty(json))
                _items = JsonSerializer.Deserialize<List<CartItem>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            _items = [];
        }
        Changed?.Invoke();
    }

    public bool Contains(string ean) => _items.Any(i => i.Ean == ean);

    public Task AddAsync(ProductDto product)
    {
        var item = _items.FirstOrDefault(i => i.Ean == product.Ean);
        var best = product.Prices.FirstOrDefault();
        if (item is null)
        {
            _items.Insert(0, new CartItem
            {
                Ean = product.Ean,
                Description = product.Description,
                Brand = product.Brand,
                Presentation = product.Presentation,
                BestPrice = best?.Price,
                BestChainName = best?.ChainName,
            });
        }
        else
        {
            item.Quantity = Math.Min(item.Quantity + 1, MaxQuantity);
            item.BestPrice = best?.Price ?? item.BestPrice;
            item.BestChainName = best?.ChainName ?? item.BestChainName;
        }
        return SaveAsync();
    }

    public Task SetQuantityAsync(string ean, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.Ean == ean);
        if (item is null) return Task.CompletedTask;
        if (quantity <= 0) _items.Remove(item);
        else item.Quantity = Math.Min(quantity, MaxQuantity);
        return SaveAsync();
    }

    public Task RemoveAsync(string ean) => SetQuantityAsync(ean, 0);

    public Task ClearAsync()
    {
        _items.Clear();
        return SaveAsync();
    }

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("superApp.storageSet", StorageKey, JsonSerializer.Serialize(_items));
        }
        catch (JSException)
        {
            // Sin almacenamiento: la lista sigue en memoria.
        }
    }
}
