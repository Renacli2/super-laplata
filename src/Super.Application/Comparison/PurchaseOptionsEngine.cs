using Super.Domain;

namespace Super.Application.Comparison;

/// <summary>
/// Una forma de hacer la compra: en qué supermercados y qué llevar en cada uno.
/// Cada producto va al supermercado más barato entre los de la opción que lo venden.
/// </summary>
/// <param name="Stores">Supermercados que se visitan (los que tienen al menos un producto asignado), en orden.</param>
/// <param name="Total">Suma de lo que se compra (sin los productos que faltan).</param>
/// <param name="Plan">Qué llevar en cada supermercado.</param>
/// <param name="MissingEans">Productos que ninguno de estos supermercados vende (pero sí otro).</param>
public sealed record PurchaseOption(
    IReadOnlyList<Chain> Stores,
    decimal Total,
    IReadOnlyDictionary<Chain, IReadOnlyList<LineResult>> Plan,
    IReadOnlyList<string> MissingEans)
{
    public bool IsComplete => MissingEans.Count == 0;
    public int StoreCount => Stores.Count;
}

/// <param name="Options">Primero las opciones completas, de la más barata a la más cara; después las incompletas.</param>
/// <param name="Recommended">La de menos supermercados entre las que cuestan como mucho <paramref name="Threshold"/> más que la más barata.</param>
/// <param name="Cheapest">La opción completa más barata.</param>
/// <param name="BestSingleStore">La compra completa más barata en un solo supermercado (null si ninguno tiene todo).</param>
/// <param name="UnavailableEans">Productos que no vende ninguna cadena: no entran en ninguna opción.</param>
/// <param name="InvalidCodes">Códigos que no son EAN comparables (inválidos o de peso variable).</param>
public sealed record PurchaseOptionsResult(
    IReadOnlyList<PurchaseOption> Options,
    PurchaseOption? Recommended,
    PurchaseOption? Cheapest,
    PurchaseOption? BestSingleStore,
    decimal Threshold,
    IReadOnlyList<string> UnavailableEans,
    IReadOnlyList<string> InvalidCodes)
{
    /// <summary>Cuánto más cuesta una opción que la más barata, como fracción (0,02 = 2 %).</summary>
    public decimal? GapToCheapest(PurchaseOption option) =>
        Cheapest is { Total: > 0m } c ? (option.Total - c.Total) / c.Total : null;
}

/// <summary>
/// Arma las formas de hacer la compra con 1, 2 o 3 supermercados:
/// <list type="bullet">
/// <item>cada supermercado solo;</item>
/// <item>cada par, repartiendo cada producto al más barato de los dos;</item>
/// <item>los tres, solo si sale más barato que cualquier opción completa de uno o dos supermercados.</item>
/// </list>
/// Las opciones que terminan visitando los mismos supermercados con el mismo reparto se muestran una sola vez.
/// </summary>
public sealed class PurchaseOptionsEngine
{
    public PurchaseOptionsResult Compute(
        IEnumerable<CartLine> cart,
        IEnumerable<PriceQuote> quotes,
        decimal threshold = ComparisonEngine.DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(quotes);
        if (threshold < 0m) throw new ArgumentOutOfRangeException(nameof(threshold), "El umbral no puede ser negativo.");

        var (available, unavailable, invalid) = Prepare(cart, quotes);
        var chains = Enum.GetValues<Chain>();

        // Todos los subconjuntos no vacíos de cadenas (con 3 cadenas: 7).
        var candidates = new List<PurchaseOption>();
        for (var mask = 1; mask < 1 << chains.Length; mask++)
        {
            var subset = chains.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            if (available.Count == 0) break;
            var option = Build(subset, available);
            if (option.StoreCount > 0) candidates.Add(option);   // una cadena que no vende nada de la lista no es opción
        }

        // Una misma compra puede salir de varios subconjuntos (p. ej. un par donde uno de los dos nunca es más barato):
        // se deja una sola, la del subconjunto más chico.
        var unique = candidates
            .GroupBy(Key)
            .Select(g => g.First())
            .ToList();

        var complete = unique.Where(o => o.IsComplete).ToList();
        var upToTwo = complete.Where(o => o.StoreCount <= 2).ToList();
        var bestUpToTwo = upToTwo.Count > 0 ? upToTwo.Min(o => o.Total) : (decimal?)null;

        // Tres supermercados: solo si mejora a todas las opciones de uno o dos.
        complete.RemoveAll(o => o.StoreCount >= 3 && bestUpToTwo is { } b && o.Total >= b);

        var orderedComplete = complete
            .OrderBy(o => o.Total)
            .ThenBy(o => o.StoreCount)
            .ThenBy(o => string.Join(",", o.Stores.Select(s => (int)s)))
            .ToList();

        var incomplete = unique
            .Where(o => !o.IsComplete && o.StoreCount <= 2)
            .OrderBy(o => o.MissingEans.Count)
            .ThenBy(o => o.StoreCount)
            .ThenBy(o => o.Total)
            .ToList();

        var cheapest = orderedComplete.FirstOrDefault();
        PurchaseOption? recommended = null;
        if (cheapest is not null)
        {
            var limit = cheapest.Total * (1m + threshold);
            recommended = orderedComplete
                .Where(o => o.Total <= limit)
                .OrderBy(o => o.StoreCount)
                .ThenBy(o => o.Total)
                .First();
        }

        var bestSingle = orderedComplete.FirstOrDefault(o => o.StoreCount == 1);

        return new PurchaseOptionsResult(
            [.. orderedComplete, .. incomplete],
            recommended,
            cheapest,
            bestSingle,
            threshold,
            unavailable,
            invalid);
    }

    private static PurchaseOption Build(IReadOnlyList<Chain> subset, IReadOnlyList<Item> items)
    {
        var lines = new List<LineResult>();
        var missing = new List<string>();
        foreach (var item in items)
        {
            var best = subset
                .Where(item.Prices.ContainsKey)
                .Select(c => (Chain: c, Price: item.Prices[c]))
                .OrderBy(p => p.Price)
                .ThenBy(p => (int)p.Chain)
                .Cast<(Chain Chain, decimal Price)?>()
                .FirstOrDefault();

            if (best is { } b) lines.Add(new LineResult(item.Ean, item.Quantity, b.Chain, b.Price));
            else missing.Add(item.Ean);
        }

        var plan = lines
            .GroupBy(l => l.Chain)
            .OrderBy(g => (int)g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LineResult>)g.ToList());

        return new PurchaseOption(plan.Keys.ToList(), lines.Sum(l => l.Total), plan, missing);
    }

    /// <summary>Identidad de una compra: qué producto va a qué supermercado, y qué falta.</summary>
    private static string Key(PurchaseOption o) =>
        string.Join(";", o.Plan.SelectMany(kv => kv.Value.Select(l => $"{l.Ean}@{(int)kv.Key}")).Order(StringComparer.Ordinal))
        + "|" + string.Join(",", o.MissingEans);

    private sealed record Item(string Ean, int Quantity, Dictionary<Chain, decimal> Prices);

    /// <summary>Unifica el carrito por EAN canónico y arma el índice de precios mínimos por cadena.</summary>
    private static (List<Item> Available, List<string> Unavailable, List<string> Invalid) Prepare(
        IEnumerable<CartLine> cart, IEnumerable<PriceQuote> quotes)
    {
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

        var book = new Dictionary<string, Dictionary<Chain, decimal>>(StringComparer.Ordinal);
        foreach (var quote in quotes)
        {
            if (quote.UnitPrice <= 0m) continue;
            var ean = Ean.Normalize(quote.Ean);
            if (ean is null) continue;
            if (!book.TryGetValue(ean, out var byChain)) book[ean] = byChain = [];
            if (!byChain.TryGetValue(quote.Chain, out var current) || quote.UnitPrice < current)
                byChain[quote.Chain] = quote.UnitPrice;
        }

        var available = new List<Item>();
        var unavailable = new List<string>();
        foreach (var (ean, qty) in quantities.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (book.TryGetValue(ean, out var prices)) available.Add(new Item(ean, qty, prices));
            else unavailable.Add(ean);
        }
        return (available, unavailable, invalid);
    }
}
