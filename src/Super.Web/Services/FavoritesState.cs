using System.Text.Json;
using Microsoft.JSInterop;
using Super.Application.Catalog;
using Super.Application.Orders;
using Super.Domain;

namespace Super.Web.Services;

/// <summary>Un producto habitual: lo que se compra seguido, con la cantidad de siempre.</summary>
public sealed class FavoriteItem
{
    /// <summary>EAN normalizado (13 dígitos), como en el catálogo.</summary>
    public required string Ean { get; init; }
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Presentation { get; set; } = "";
    public int Quantity { get; set; } = 1;

    public CatalogEntry ToEntry() => new(Ean, Description, Brand, Presentation);
}

/// <summary>
/// Los productos habituales de este celular, marcados con la estrella. Se guardan en el navegador (localStorage),
/// aparte de la lista: vaciar la lista o confirmar una compra no los toca.
/// </summary>
public sealed class FavoritesState(IJSRuntime js)
{
    private const string StorageKey = "super.habituales.v1";
    private List<FavoriteItem> _items = [];
    private HashSet<string> _eans = new(StringComparer.Ordinal);
    private bool _loaded;

    public event Action? Changed;

    /// <summary>Ordenados por nombre, sin distinguir mayúsculas ni acentos.</summary>
    public IReadOnlyList<FavoriteItem> Items => _items;
    public int Count => _items.Count;

    /// <summary>EAN de los habituales, para que la búsqueda los ponga primero.</summary>
    public IReadOnlySet<string> Eans => _eans;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await js.InvokeAsync<string?>("superApp.storageGet", StorageKey);
            if (!string.IsNullOrEmpty(json))
                _items = JsonSerializer.Deserialize<List<FavoriteItem>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            _items = [];
        }
        Reindex();
        Changed?.Invoke();
    }

    public bool IsFavorite(string ean) => _eans.Contains(Key(ean));

    /// <summary>Marca o desmarca un producto. Al marcarlo guarda la cantidad (la de la lista, si se marca desde ahí).</summary>
    /// <returns>true si quedó marcado.</returns>
    public async Task<bool> ToggleAsync(CatalogEntry product, int quantity = 1)
    {
        var key = Key(product.Ean);
        var existing = _items.FirstOrDefault(f => f.Ean == key);
        if (existing is not null)
        {
            _items.Remove(existing);
        }
        else
        {
            _items.Add(new FavoriteItem
            {
                Ean = key,
                Description = product.Description,
                Brand = product.Brand,
                Presentation = product.Presentation,
                Quantity = Math.Clamp(quantity, 1, OrderLink.MaxQuantity),
            });
        }
        Reindex();
        await SaveAsync();
        return existing is null;
    }

    private static string Key(string ean) => Ean.Normalize(ean) ?? ean;

    private void Reindex()
    {
        _items = _items
            .OrderBy(f => CatalogSearch.Fold(f.Description), StringComparer.Ordinal)
            .ThenBy(f => f.Ean, StringComparer.Ordinal)
            .ToList();
        _eans = _items.Select(f => f.Ean).ToHashSet(StringComparer.Ordinal);
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
