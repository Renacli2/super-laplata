using System.Net.Http.Json;
using Super.Application.Catalog;
using Super.Application.Comparison;
using Super.Contracts;
using Super.Domain;

namespace Super.Web.Services;

/// <summary>Error al cargar los datos, con un mensaje para mostrar.</summary>
public sealed class DataException(string message) : Exception(message);

/// <summary>
/// Datos que publica Super.Export (carpeta data/ junto a la app). El service worker guarda la última copia,
/// así la app busca y compara aunque no haya señal.
/// </summary>
public sealed class DataStore(HttpClient http)
{
    private DataVersionDto? _version;
    private CatalogSearch? _catalog;
    private IReadOnlyList<CatalogEntry> _entries = [];
    private Task<EquivalentFinder>? _equivalentsLoad;
    private Task<CatalogSearch>? _catalogLoad;
    private Task<PricesFileDto>? _pricesLoad;

    /// <summary>Cabecera que pone el service worker cuando responde con la copia guardada (sin señal).</summary>
    private const string SavedCopyHeader = "x-super-saved";

    public DataVersionDto? Version => _version;

    /// <summary>La última descarga de datos vino de la copia guardada en el celular, no de internet.</summary>
    public bool UsingSavedData { get; private set; }

    /// <summary>Cambió <see cref="Version"/> o <see cref="UsingSavedData"/>.</summary>
    public event Action? Changed;

    /// <summary>Días desde la fecha de los precios publicados; null si todavía no se sabe.</summary>
    public int? AgeInDays => _version is null ? null : Freshness.AgeInDays(_version.PriceDate, Freshness.Today());

    public bool IsStale => _version is not null && Freshness.IsStale(_version.PriceDate, Freshness.Today());

    public async Task<DataVersionDto?> GetVersionAsync()
    {
        if (_version is not null) return _version;
        try
        {
            _version = await GetJsonAsync<DataVersionDto>($"data/version.json?t={DateTime.UtcNow.Ticks}");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _version = null;
        }
        Changed?.Invoke();
        return _version;
    }

    /// <summary>Catálogo para buscar (se descarga una vez por sesión, ~600 KB comprimido).</summary>
    public Task<CatalogSearch> GetCatalogAsync() => _catalogLoad ??= LoadCatalogAsync();

    private async Task<CatalogSearch> LoadCatalogAsync()
    {
        try
        {
            var version = await GetVersionAsync();
            var file = await GetJsonAsync<CatalogFileDto>($"data/catalog.json?v={version?.PriceDate:yyyyMMdd}")
                       ?? throw new DataException("El catálogo de productos está vacío.");
            _entries = file.Products.Select(p => new CatalogEntry(p.Ean, p.Description, p.Brand, p.Presentation)).ToList();
            return _catalog = new CatalogSearch(_entries);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _catalogLoad = null;   // permitir reintentar
            throw new DataException("No se pudo cargar la lista de productos. Revisá la conexión y volvé a abrir la app.");
        }
    }

    /// <summary>Precios por cadena (solo para comparar, ~300 KB comprimido).</summary>
    public Task<PricesFileDto> GetPricesAsync() => _pricesLoad ??= LoadPricesAsync();

    /// <summary>
    /// Baja los precios del día apenas se abre la app (solo el administrador), así quedan guardados en el celular
    /// y la comparación anda aunque en el súper no haya señal. Si falla, no molesta: se reintenta al comparar.
    /// </summary>
    public async Task PrefetchPricesAsync()
    {
        try { await GetPricesAsync(); }
        catch (DataException) { }
    }

    private async Task<PricesFileDto> LoadPricesAsync()
    {
        try
        {
            var version = await GetVersionAsync();
            return await GetJsonAsync<PricesFileDto>($"data/prices.json?v={version?.PriceDate:yyyyMMdd}")
                   ?? throw new DataException("El archivo de precios está vacío.");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _pricesLoad = null;   // permitir reintentar
            throw new DataException("No se pudieron cargar los precios y no hay una copia guardada en el celular. Probá de nuevo cuando tengas señal.");
        }
    }

    /// <summary>
    /// Buscador de productos equivalentes más baratos (solo el administrador: usa los precios).
    /// Se arma una vez por sesión con el catálogo y el precio más bajo de cada producto.
    /// </summary>
    public Task<EquivalentFinder> GetEquivalentsAsync() => _equivalentsLoad ??= LoadEquivalentsAsync();

    private async Task<EquivalentFinder> LoadEquivalentsAsync()
    {
        try
        {
            await GetCatalogAsync();
            var file = await GetPricesAsync();
            return new EquivalentFinder(_entries, BestPrices(file));
        }
        catch
        {
            _equivalentsLoad = null;   // permitir reintentar
            throw;
        }
    }

    /// <summary>Precio más bajo de cada EAN entre las cadenas, y en cuáles está a ese precio.</summary>
    public static IReadOnlyDictionary<string, BestPrice> BestPrices(PricesFileDto file)
    {
        var chains = file.Chains
            .Select(c => Enum.TryParse<Chain>(c.Id, out var chain) ? chain : (Chain?)null)
            .ToList();
        var result = new Dictionary<string, BestPrice>(file.Prices.Count, StringComparer.Ordinal);
        foreach (var row in file.Prices)
        {
            decimal? min = null;
            for (var i = 0; i < row.Prices.Count && i < chains.Count; i++)
                if (row.Prices[i] is { } p && chains[i] is not null && (min is null || p < min)) min = p;
            if (min is not { } best) continue;
            var at = new List<Chain>();
            for (var i = 0; i < row.Prices.Count && i < chains.Count; i++)
                if (row.Prices[i] == best && chains[i] is { } chain) at.Add(chain);
            result[row.Ean] = new BestPrice(best, at);
        }
        return result;
    }

    /// <summary>Descarga un JSON de data/ y anota si vino de la copia guardada por el service worker.</summary>
    private async Task<T?> GetJsonAsync<T>(string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var saved = response.Headers.Contains(SavedCopyHeader) || response.Content.Headers.Contains(SavedCopyHeader);
        if (saved != UsingSavedData)
        {
            UsingSavedData = saved;
            Changed?.Invoke();
        }
        return await response.Content.ReadFromJsonAsync<T>();
    }

    /// <summary>Precios de los EAN pedidos, listos para el motor de comparación.</summary>
    public static IReadOnlyList<PriceQuote> QuotesFor(PricesFileDto file, IEnumerable<string> eans)
    {
        var wanted = eans.Select(Ean.Normalize).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var chains = file.Chains
            .Select(c => Enum.TryParse<Chain>(c.Id, out var chain) ? chain : (Chain?)null)
            .ToList();

        var quotes = new List<PriceQuote>();
        foreach (var row in file.Prices)
        {
            if (!wanted.Contains(row.Ean)) continue;
            for (var i = 0; i < row.Prices.Count && i < chains.Count; i++)
            {
                if (row.Prices[i] is { } price && chains[i] is { } chain)
                    quotes.Add(new PriceQuote(chain, row.Ean, price));
            }
        }
        return quotes;
    }
}
