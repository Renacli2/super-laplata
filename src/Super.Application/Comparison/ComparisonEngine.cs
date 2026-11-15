using Super.Domain;

namespace Super.Application.Comparison;

/// <summary>
/// Motor de comparación. Regla:
/// 1. Por ítem se toma el precio mínimo entre cadenas → total repartido (óptimo).
/// 2. Se calcula el total del carrito completo en cada cadena que tenga todos los ítems disponibles.
/// 3. brecha = (mejor total en una sola cadena − total repartido) / total repartido.
///    Si brecha ≤ umbral → se propone la cadena única; si no → una lista por cadena.
/// Los ítems que no existen en ninguna cadena se excluyen de todos los totales y se informan aparte.
/// </summary>
public sealed class ComparisonEngine
{
    /// <summary>Umbral por defecto acordado: 3 % (configurable por grupo).</summary>
    public const decimal DefaultThreshold = 0.03m;

    public ComparisonResult Compare(
        IEnumerable<CartLine> cart,
        IEnumerable<PriceQuote> quotes,
        decimal threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(quotes);
        if (threshold < 0m) throw new ArgumentOutOfRangeException(nameof(threshold), "El umbral no puede ser negativo.");

        // 1) Unificar el carrito por EAN canónico.
        var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
        var invalid = new List<string>();
        foreach (var line in cart)
        {
            if (line.Quantity <= 0) continue;
            var ean = Ean.Normalize(line.Ean);
            if (ean is null || Ean.IsVariableWeight(ean))
            {
                invalid.Add(line.Ean);
                continue;
            }
            quantities[ean] = quantities.GetValueOrDefault(ean) + line.Quantity;
        }

        // 2) Índice de precios: ean → (cadena → precio mínimo).
        var book = new Dictionary<string, Dictionary<Chain, decimal>>(StringComparer.Ordinal);
        foreach (var quote in quotes)
        {
            if (quote.UnitPrice <= 0m) continue;
            var ean = Ean.Normalize(quote.Ean);
            if (ean is null) continue;

            if (!book.TryGetValue(ean, out var byChain))
            {
                byChain = new Dictionary<Chain, decimal>();
                book[ean] = byChain;
            }

            if (!byChain.TryGetValue(quote.Chain, out var current) || quote.UnitPrice < current)
                byChain[quote.Chain] = quote.UnitPrice;
        }

        // 3) Separar disponibles / no disponibles (orden estable por EAN).
        var available = new List<(string Ean, int Qty, Dictionary<Chain, decimal> Prices)>();
        var unavailable = new List<string>();
        foreach (var (ean, qty) in quantities.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (book.TryGetValue(ean, out var prices)) available.Add((ean, qty, prices));
            else unavailable.Add(ean);
        }

        // 4) Compra repartida: cada ítem a su cadena más barata (empate → menor valor del enum).
        var splitLines = available
            .Select(a =>
            {
                var best = a.Prices.OrderBy(p => p.Value).ThenBy(p => (int)p.Key).First();
                return new LineResult(a.Ean, a.Qty, best.Key, best.Value);
            })
            .ToList();
        var splitTotal = splitLines.Sum(l => l.Total);

        // 5) Totales por cadena (solo válidos con cobertura completa).
        var summaries = new List<ChainSummary>();
        foreach (var chain in Enum.GetValues<Chain>())
        {
            var covered = available.Count(a => a.Prices.ContainsKey(chain));
            decimal? total = available.Count > 0 && covered == available.Count
                ? available.Sum(a => a.Prices[chain] * a.Qty)
                : null;
            summaries.Add(new ChainSummary(chain, covered, available.Count, total));
        }

        var bestSingle = summaries
            .Where(s => s.Total.HasValue)
            .OrderBy(s => s.Total)
            .ThenBy(s => (int)s.Chain)
            .FirstOrDefault();

        // 6) Decisión.
        decimal? gap = null;
        var recommendSingle = false;
        if (bestSingle is not null && splitTotal > 0m)
        {
            gap = (bestSingle.Total!.Value - splitTotal) / splitTotal;
            recommendSingle = gap.Value <= threshold;
        }

        IReadOnlyDictionary<Chain, IReadOnlyList<LineResult>> plan;
        if (recommendSingle)
        {
            var chain = bestSingle!.Chain;
            var lines = available
                .Select(a => new LineResult(a.Ean, a.Qty, chain, a.Prices[chain]))
                .ToList();
            plan = new Dictionary<Chain, IReadOnlyList<LineResult>> { [chain] = lines };
        }
        else
        {
            plan = splitLines
                .GroupBy(l => l.Chain)
                .OrderBy(g => (int)g.Key)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<LineResult>)g.ToList());
        }

        return new ComparisonResult(
            splitTotal,
            bestSingle?.Chain,
            bestSingle?.Total,
            gap,
            threshold,
            recommendSingle,
            plan,
            summaries,
            unavailable,
            invalid);
    }
}
