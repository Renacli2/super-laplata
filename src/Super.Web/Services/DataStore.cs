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
    private PricesFileDto? _prices;
    private Task<CatalogSearch>? _catalogLoad;

    public DataVersionDto? Version => _version;

    public async Task<DataVersionDto?> GetVersionAsync()
    {
        if (_version is not null) return _version;
        try
        {
            _version = await http.GetFromJsonAsync<DataVersionDto>($"data/version.json?t={DateTime.UtcNow.Ticks}");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _version = null;
        }
        return _version;
    }

    /// <summary>Catálogo para buscar (se descarga una vez por sesión, ~600 KB comprimido).</summary>
    public Task<CatalogSearch> GetCatalogAsync() => _catalogLoad ??= LoadCatalogAsync();

    private async Task<CatalogSearch> LoadCatalogAsync()
    {
        try
        {
            var version = await GetVersionAsync();
            var file = await http.GetFromJsonAsync<CatalogFileDto>($"data/catalog.json?v={version?.PriceDate:yyyyMMdd}")
                       ?? throw new DataException("El catálogo de productos está vacío.");
            return _catalog = new CatalogSearch(file.Products.Select(p =>
                new CatalogEntry(p.Ean, p.Description, p.Brand, p.Presentation)));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _catalogLoad = null;   // permitir reintentar
            throw new DataException("No se pudo cargar la lista de productos. Revisá la conexión y volvé a abrir la app.");
        }
    }

    /// <summary>Precios por cadena (solo para comparar, ~300 KB comprimido).</summary>
    public async Task<PricesFileDto> GetPricesAsync()
    {
        if (_prices is not null) return _prices;
        try
        {
            var version = await GetVersionAsync();
            return _prices = await http.GetFromJsonAsync<PricesFileDto>($"data/prices.json?v={version?.PriceDate:yyyyMMdd}")
                             ?? throw new DataException("El archivo de precios está vacío.");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            throw new DataException("No se pudieron cargar los precios. Revisá la conexión y probá de nuevo.");
        }
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
