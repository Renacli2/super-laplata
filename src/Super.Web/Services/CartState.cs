using System.Text.Json;
using Microsoft.JSInterop;
using Super.Application.Catalog;
using Super.Application.Comparison;
using Super.Application.Orders;
using Super.Domain;

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

    /// <summary>
    /// Ya está en el changuito (se tachó en la comparación). Se guarda en el producto, no en el plan,
    /// así sigue tachado aunque se recalcule y el producto pase a otro supermercado.
    /// </summary>
    public bool InCart { get; set; }
}

/// <summary>Un producto de una compra confirmada, con el precio estimado (SEPA) de la opción que se estaba usando.</summary>
public sealed record PurchasedItem(string Ean, string Description, int Quantity, string? Chain, decimal? Total, List<string> RequestedBy);

/// <summary>Una compra confirmada: lo que estaba tildado cuando se tocó "Confirmar compra".</summary>
public sealed record Purchase(DateTimeOffset At, List<PurchasedItem> Items)
{
    /// <summary>Total estimado: solo lo que tenía precio.</summary>
    public decimal EstimatedTotal => Items.Sum(i => i.Total ?? 0m);
}

/// <summary>
/// La lista, guardada en el navegador (localStorage) para que sobreviva al cerrar la app.
/// Si el navegador no deja guardar (modo privado), funciona igual mientras la app esté abierta.
/// </summary>
public sealed class CartState(IJSRuntime js, TimeProvider time)
{
    private const string StorageKey = "super.lista.v2";
    private const string PurchasesKey = "super.compras.v1";

    /// <summary>Compras que se guardan (las más viejas se descartan): alcanza para aprender los habituales.</summary>
    public const int MaxPurchases = 20;

    private List<CartItem> _items = [];
    private List<Purchase> _purchases = [];
    private bool _loaded;

    // Para deshacer la última compra: la lista de antes, válida mientras no se cambie nada más.
    private List<CartItem>? _beforePurchase;
    private int _version;
    private int _undoVersion = -1;

    public event Action? Changed;

    public IReadOnlyList<CartItem> Items => _items;
    public int Count => _items.Count;
    public int InCartCount => _items.Count(i => i.InCart);

    /// <summary>Compras confirmadas, la más reciente al final.</summary>
    public IReadOnlyList<Purchase> Purchases => _purchases;

    /// <summary>Se puede deshacer la última compra confirmada: la lista no cambió desde entonces.</summary>
    public bool CanUndoPurchase => _beforePurchase is not null && _undoVersion == _version;

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
        try
        {
            var json = await js.InvokeAsync<string?>("superApp.storageGet", PurchasesKey);
            if (!string.IsNullOrEmpty(json))
                _purchases = JsonSerializer.Deserialize<List<Purchase>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            _purchases = [];
        }
        Changed?.Invoke();
    }

    public bool Contains(string ean) => _items.Any(i => i.Ean == ean);

    public Task AddAsync(CatalogEntry product, int quantity = 1) =>
        AddCoreAsync(product.Ean, product, Math.Max(quantity, 1), from: null, save: true);

    /// <summary>Agrega varios productos de una vez (los habituales), guardando una sola vez.</summary>
    public async Task AddManyAsync(IEnumerable<(CatalogEntry Product, int Quantity)> products)
    {
        foreach (var (product, quantity) in products)
            await AddCoreAsync(product.Ean, product, Math.Max(quantity, 1), from: null, save: false);
        await SaveAsync();
    }

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
        if (quantity <= 0)
        {
            _items.Remove(item);
        }
        else
        {
            // Si piden más de algo que ya estaba en el changuito, hay que volver a buscarlo.
            if (quantity > item.Quantity) item.InCart = false;
            item.Quantity = Math.Min(quantity, OrderLink.MaxQuantity);
        }
        return SaveAsync();
    }

    /// <summary>
    /// Cambia un producto por otro equivalente: misma cantidad y mismos "pidió", en el mismo lugar de la lista.
    /// Si el nuevo ya estaba en la lista, se suman las cantidades.
    /// </summary>
    public Task ReplaceAsync(string ean, CatalogEntry replacement)
    {
        var item = _items.FirstOrDefault(i => i.Ean == ean);
        if (item is null || ean == replacement.Ean) return Task.CompletedTask;

        var existing = _items.FirstOrDefault(i => i.Ean == replacement.Ean);
        if (existing is not null)
        {
            existing.Quantity = Math.Min(existing.Quantity + item.Quantity, OrderLink.MaxQuantity);
            existing.InCart = false;
            foreach (var who in item.RequestedBy.Where(w => !existing.RequestedBy.Contains(w)))
                existing.RequestedBy.Add(who);
            _items.Remove(item);
        }
        else
        {
            _items[_items.IndexOf(item)] = new CartItem
            {
                Ean = replacement.Ean,
                Description = replacement.Description,
                Brand = replacement.Brand,
                Presentation = replacement.Presentation,
                Quantity = item.Quantity,
                RequestedBy = item.RequestedBy,
            };
        }
        return SaveAsync();
    }

    public Task SetInCartAsync(string ean, bool inCart)
    {
        var item = _items.FirstOrDefault(i => i.Ean == ean);
        if (item is null || item.InCart == inCart) return Task.CompletedTask;
        item.InCart = inCart;
        return SaveAsync();
    }

    public Task ClearInCartAsync()
    {
        foreach (var item in _items) item.InCart = false;
        return SaveAsync();
    }

    /// <summary>
    /// Confirma la compra: saca de la lista lo tildado y lo anota en el registro de compras.
    /// Lo que no se tildó queda en la lista para otra vuelta. Devuelve null si no había nada tildado.
    /// </summary>
    /// <param name="lines">Líneas de la opción que se estaba usando, por EAN normalizado: de ahí salen cadena y precio.</param>
    public async Task<Purchase?> ConfirmPurchaseAsync(IReadOnlyDictionary<string, LineResult> lines)
    {
        var picked = _items.Where(i => i.InCart).ToList();
        if (picked.Count == 0) return null;

        var purchase = new Purchase(time.GetUtcNow(), picked.Select(i =>
        {
            var line = lines.GetValueOrDefault(Ean.Normalize(i.Ean) ?? i.Ean);
            return new PurchasedItem(i.Ean, i.Description, i.Quantity, line?.Chain.ToString(), line?.Total, [.. i.RequestedBy]);
        }).ToList());

        _beforePurchase = [.. _items];
        _items = _items.Where(i => !i.InCart).ToList();
        _purchases.Add(purchase);
        if (_purchases.Count > MaxPurchases) _purchases.RemoveRange(0, _purchases.Count - MaxPurchases);

        await SaveAsync();
        await SavePurchasesAsync();
        _undoVersion = _version;
        return purchase;
    }

    /// <summary>Deshace la última compra confirmada: la lista vuelve como estaba (con lo tildado) y se borra del registro.</summary>
    public async Task<bool> UndoPurchaseAsync()
    {
        if (!CanUndoPurchase) return false;
        _items = _beforePurchase!;
        _beforePurchase = null;
        if (_purchases.Count > 0) _purchases.RemoveAt(_purchases.Count - 1);
        await SaveAsync();
        await SavePurchasesAsync();
        return true;
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
        item.InCart = false;   // hay que llevar más: deja de estar tachado
        if (!string.IsNullOrWhiteSpace(from) && !item.RequestedBy.Contains(from)) item.RequestedBy.Add(from);
        return save ? SaveAsync() : Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        _version++;   // cualquier cambio en la lista deja sin efecto el "Deshacer" de la última compra
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("superApp.storageSet", StorageKey, JsonSerializer.Serialize(_items));
        }
        catch (JSException) { }
    }

    private async Task SavePurchasesAsync()
    {
        try
        {
            await js.InvokeVoidAsync("superApp.storageSet", PurchasesKey, JsonSerializer.Serialize(_purchases));
        }
        catch (JSException) { }
    }
}
