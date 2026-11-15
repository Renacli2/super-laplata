using System.Text.Json;
using Microsoft.JSInterop;
using Super.Application.Catalog;
using Super.Application.Orders;

namespace Super.Web.Services;

public sealed class CartItem
{
    public required string Ean { get; init; }
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Presentation { get; set; } = "";
    public int Quantity { get; set; } = 1;

    /// <summary>Quiénes pidieron este producto (solo en la lista del administrador).</summary>
    public List<string> RequestedBy { get; set; } = [];
}

/// <summary>
/// La lista, guardada en el navegador (localStorage) para que sobreviva al cerrar la app.
/// Si el navegador no deja guardar (modo privado), funciona igual mientras la app esté abierta.
/// </summary>
public sealed class CartState(IJSRuntime js)
{
    private const string StorageKey = "super.lista.v2";
    private List<CartItem> _items = [];
    private bool _loaded;

    public event Action? Changed;

    public IReadOnlyList<CartItem> Items => _items;
    public int Count => _items.Count;

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

    public Task AddAsync(CatalogEntry product) => AddCoreAsync(product.Ean, product, 1, from: null, save: true);

    /// <summary>Suma un pedido recibido: acumula cantidades y anota quién lo pidió.</summary>
    public async Task AddOrderAsync(Order order, CatalogSearch catalog)
    {
        foreach (var item in order.Items)
            await AddCoreAsync(item.Ean, catalog.Find(item.Ean), item.Quantity, order.From, save: false);
        await SaveAsync();
    }

    public Order ToOrder(string from) => new(from, _items.Select(i => new OrderItem(i.Ean, i.Quantity)).ToList());

    public Task SetQuantityAsync(string ean, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.Ean == ean);
        if (item is null) return Task.CompletedTask;
        if (quantity <= 0) _items.Remove(item);
        else item.Quantity = Math.Min(quantity, OrderLink.MaxQuantity);
        return SaveAsync();
    }

    public Task ClearAsync()
    {
        _items.Clear();
        return SaveAsync();
    }

    private Task AddCoreAsync(string ean, CatalogEntry? product, int quantity, string? from, bool save)
    {
        var item = _items.FirstOrDefault(i => i.Ean == ean);
        if (item is null)
        {
            item = new CartItem
            {
                Ean = ean,
                Description = product?.Description ?? $"Código {ean.TrimStart('0')}",
                Brand = product?.Brand ?? "",
                Presentation = product?.Presentation ?? "",
                Quantity = 0,
            };
            _items.Insert(0, item);
        }
        item.Quantity = Math.Min(item.Quantity + quantity, OrderLink.MaxQuantity);
        if (!string.IsNullOrWhiteSpace(from) && !item.RequestedBy.Contains(from)) item.RequestedBy.Add(from);
        return save ? SaveAsync() : Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("superApp.storageSet", StorageKey, JsonSerializer.Serialize(_items));
        }
        catch (JSException) { }
    }
}
