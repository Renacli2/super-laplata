using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Super.Application.Comparison;
using Super.Contracts;
using Super.Domain;
using Super.Infrastructure.Persistence;

namespace Super.Api;

/// <summary>Consultas de catálogo y comparación que expone la API.</summary>
public sealed class CatalogService(SuperDbContext db, IPriceQuoteSource prices)
{
    public const int MaxSearchResults = 30;
    public const int MaxCartItems = 200;
    public const int MaxQuantity = 999;

    /// <summary>
    /// Busca por texto (todas las palabras deben aparecer en la descripción o la marca) o por código
    /// (si el texto es numérico). Solo devuelve productos con precio en el último día importado.
    /// </summary>
    public async Task<IReadOnlyList<ProductDto>> SearchAsync(string? text, int limit, CancellationToken ct)
    {
        var q = (text ?? "").Trim();
        if (q.Length < 2) return [];
        limit = Math.Clamp(limit, 1, MaxSearchResults);

        var since = await OldestLatestDateAsync(ct);
        if (since is null) return [];

        var query = db.Products.AsNoTracking().Where(p => p.LastSeenOn >= since.Value);

        if (q.All(char.IsAsciiDigit))
        {
            var exact = Ean.Normalize(q);
            query = query.Where(p => p.Ean == exact || p.Ean.Contains(q));
        }
        else
        {
            foreach (var word in q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6))
            {
                var pattern = "%" + EscapeLike(word) + "%";
                query = query.Where(p => EF.Functions.ILike(p.Description, pattern, "\\")
                                      || EF.Functions.ILike(p.Brand, pattern, "\\"));
            }
        }

        var found = await query
            .OrderBy(p => p.Description.Length)
            .ThenBy(p => p.Description)
            .Take(limit)
            .ToListAsync(ct);

        return await WithPricesAsync(found, ct);
    }

    /// <summary>Producto por código de barras (acepta el código tal como lo lee la cámara).</summary>
    public async Task<ProductDto?> FindAsync(string code, CancellationToken ct)
    {
        var ean = Ean.Normalize(code);
        if (ean is null) return null;
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Ean == ean, ct);
        if (product is null) return null;
        var dto = (await WithPricesAsync([product], ct)).Single();
        return dto.Prices.Count == 0 ? null : dto;
    }

    public async Task<CompareResponse> CompareAsync(CompareRequest request, CancellationToken ct)
    {
        var cart = request.Items.Select(i => new CartLine(i.Ean, i.Quantity)).ToList();
        var quotes = await prices.GetQuotesAsync(cart.Select(c => c.Ean), ct);
        var result = new ComparisonEngine().Compare(cart, quotes, request.Threshold ?? ComparisonEngine.DefaultThreshold);

        var eans = result.Plan.Values.SelectMany(l => l).Select(l => l.Ean).Distinct().ToList();
        var names = await db.Products.AsNoTracking()
            .Where(p => eans.Contains(p.Ean))
            .ToDictionaryAsync(p => p.Ean, p => p.Description, ct);

        return new CompareResponse(
            result.SplitTotal,
            result.BestSingleChain?.ToString(),
            result.BestSingleChain?.DisplayName(),
            result.BestSingleTotal,
            result.Gap is { } g ? Math.Round(g, 4) : null,
            result.Threshold,
            result.RecommendSingleStore,
            result.ChainSummaries
                .Select(s => new ChainTotalDto(s.Chain.ToString(), s.Chain.DisplayName(), s.CoveredItems, s.TotalItems, s.Total))
                .ToList(),
            result.Plan
                .Select(kv => new PlanGroupDto(
                    kv.Key.ToString(),
                    kv.Key.DisplayName(),
                    kv.Value.Sum(l => l.Total),
                    kv.Value.Select(l => new PlanLineDto(l.Ean, names.GetValueOrDefault(l.Ean, l.Ean), l.Quantity, l.UnitPrice, l.Total)).ToList()))
                .ToList(),
            result.UnavailableEans,
            result.InvalidCodes);
    }

    public async Task<StatusDto> StatusAsync(CancellationToken ct)
    {
        var runs = await db.ImportRuns.AsNoTracking().ToListAsync(ct);
        var latest = runs
            .GroupBy(r => r.Chain)
            .Select(g => g.MaxBy(r => r.PriceDate)!)
            .OrderBy(r => r.Chain)
            .Select(r => new ChainImportDto(r.Chain.ToString(), r.Chain.DisplayName(), r.PriceDate, r.DistinctProducts))
            .ToList();
        return new StatusDto(latest, await db.Products.CountAsync(ct));
    }

    private async Task<DateOnly?> OldestLatestDateAsync(CancellationToken ct)
    {
        var latest = await db.ImportRuns
            .GroupBy(r => r.Chain)
            .Select(g => g.Max(r => r.PriceDate))
            .ToListAsync(ct);
        return latest.Count == 0 ? null : latest.Min();
    }

    private async Task<IReadOnlyList<ProductDto>> WithPricesAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        var quotes = await prices.GetQuotesAsync(products.Select(p => p.Ean), ct);
        var byEan = quotes.ToLookup(q => q.Ean);
        return products
            .Select(p => new ProductDto(
                p.Ean,
                p.Description,
                p.Brand,
                Presentation(p),
                byEan[p.Ean]
                    .OrderBy(q => q.UnitPrice).ThenBy(q => q.Chain)
                    .Select(q => new ChainPriceDto(q.Chain.ToString(), q.Chain.DisplayName(), q.UnitPrice))
                    .ToList()))
            .ToList();
    }

    private static string Presentation(Product p) =>
        p.PresentationQuantity is { } qty && p.PresentationUnit.Length > 0
            ? $"{qty.ToString("0.###", CultureInfo.InvariantCulture)} {p.PresentationUnit}"
            : "";

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
