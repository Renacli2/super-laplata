using Microsoft.EntityFrameworkCore;
using Super.Application.Comparison;
using Super.Domain;

namespace Super.Infrastructure.Persistence;

/// <summary>Lee de <see cref="ChainPrice"/> el último día importado de cada cadena.</summary>
public sealed class EfPriceQuoteSource(SuperDbContext db) : IPriceQuoteSource
{
    public async Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(IEnumerable<string> eans, CancellationToken ct = default)
    {
        var wanted = eans.Select(Ean.Normalize).OfType<string>().Distinct().ToList();
        if (wanted.Count == 0) return [];

        var latest = await db.ImportRuns
            .GroupBy(r => r.Chain)
            .Select(g => new { Chain = g.Key, Date = g.Max(r => r.PriceDate) })
            .ToListAsync(ct);

        var quotes = new List<PriceQuote>();
        foreach (var l in latest)
        {
            var rows = await db.ChainPrices.AsNoTracking()
                .Where(p => p.Chain == l.Chain && p.PriceDate == l.Date && wanted.Contains(p.Ean))
                .Select(p => new { p.Ean, p.MinListPrice })
                .ToListAsync(ct);
            quotes.AddRange(rows.Select(r => new PriceQuote(l.Chain, r.Ean, r.MinListPrice)));
        }
        return quotes;
    }
}
