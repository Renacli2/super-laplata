using Super.Domain;

namespace Super.Application.Comparison;

/// <summary>
/// Un "presupuesto": hacer la compra en un supermercado, un par o los tres. Cada producto va al más barato de esos
/// supermercados que lo vende. Lo que ninguno de ellos vende (faltantes) se completa en la cadena donde está más barato,
/// para poder comparar todas las opciones por lo que cuesta la lista entera.
/// </summary>
/// <param name="Stores">Supermercados del presupuesto (los que tienen al menos un producto asignado).</param>
/// <param name="Total">Lo que se compra en esos supermercados.</param>
/// <param name="Plan">Qué llevar en cada uno de esos supermercados.</param>
/// <param name="MissingEans">Productos que no vende ninguno de esos supermercados (pero sí otra cadena).</param>
/// <param name="Complement">Dónde conviene comprar los faltantes: cada uno en la cadena donde está más barato.</param>
/// <param name="MainStores">Supermercados con más del umbral de parada corta del valor de la compra (incluye el complemento).</param>
/// <param name="ShortStops">Supermercados con una parte chica de la compra (hasta el umbral): "paradas cortas".</param>
/// <param name="Effort">Vueltas: 1 por supermercado principal y 0,5 por parada corta.</param>
public sealed record PurchaseOption(
    IReadOnlyList<Chain> Stores,
    decimal Total,
    IReadOnlyDictionary<Chain, IReadOnlyList<LineResult>> Plan,
    IReadOnlyList<string> MissingEans,
    IReadOnlyDictionary<Chain, IReadOnlyList<LineResult>> Complement,
    IReadOnlyList<Chain> MainStores,
    IReadOnlyList<Chain> ShortStops,
    decimal Effort)
{
    public bool IsComplete => MissingEans.Count == 0;
    public int StoreCount => Stores.Count;
    public decimal ComplementTotal => Complement.Values.SelectMany(l => l).Sum(l => l.Total);

    /// <summary>Lo que cuesta la lista entera con esta opción (incluye los faltantes a su menor precio).</summary>
    public decimal FullTotal => Total + ComplementTotal;

    /// <summary>Parte del valor de la compra que falta en esta opción (0,06 = 6 %).</summary>
    public decimal MissingShare => FullTotal > 0m ? ComplementTotal / FullTotal : 0m;
}

/// <param name="Options">Todas las opciones, de la lista entera más barata a la más cara.</param>
/// <param name="Recommended">La de menos vueltas entre las que cuestan como mucho <paramref name="Threshold"/> más que la más barata.</param>
/// <param name="Cheapest">La opción con la lista entera más barata.</param>
/// <param name="BestSingleStore">La compra completa más barata en un solo supermercado (null si ninguno tiene todo).</param>
/// <param name="ShortStopShare">Hasta qué parte del valor de la compra un supermercado cuenta como parada corta.</param>
public sealed record PurchaseOptionsResult(
    IReadOnlyList<PurchaseOption> Options,
    PurchaseOption? Recommended,
    PurchaseOption? Cheapest,
    PurchaseOption? BestSingleStore,
    decimal Threshold,
    decimal ShortStopShare,
    IReadOnlyList<string> UnavailableEans,
    IReadOnlyList<string> InvalidCodes)
{
    /// <summary>Cuánto más cuesta la lista entera con una opción que con la más barata (0,02 = 2 %).</summary>
    public decimal? GapToCheapest(PurchaseOption option) =>
        Cheapest is { FullTotal: > 0m } c ? (option.FullTotal - c.FullTotal) / c.FullTotal : null;

    /// <summary>Si la opción puede recomendarse: completa, o le falta como mucho la parte de una parada corta.</summary>
    public bool IsEligible(PurchaseOption option) => option.IsComplete || option.MissingShare <= ShortStopShare;
}

/// <summary>
/// Arma los presupuestos con 1, 2 o 3 supermercados y recomienda uno ponderando el gasto contra las vueltas:
/// <list type="bullet">
/// <item>cada supermercado solo y cada par; los tres solo si salen más baratos que cualquier opción completa de uno o dos;</item>
/// <item>todas se comparan por la lista entera (lo que falta, a su menor precio en otra cadena);</item>
/// <item>un supermercado con hasta el 5 % del valor de la compra es una parada corta y cuenta media vuelta;</item>
/// <item>se recomienda la de menos vueltas entre las que cuestan como mucho un 3 % más que la más barata
/// (sin contar las que dejan afuera más del 5 % de la compra).</item>
/// </list>
/// Dos opciones que terminan siendo la misma compra (mismo producto en el mismo supermercado) se muestran una vez.
/// </summary>
public sealed class PurchaseOptionsEngine
{
    public const decimal DefaultShortStopShare = 0.05m;

    public PurchaseOptionsResult Compute(
        IEnumerable<CartLine> cart,
        IEnumerable<PriceQuote> quotes,
        decimal threshold = ComparisonEngine.DefaultThreshold,
        decimal shortStopShare = DefaultShortStopShare)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(quotes);
        if (threshold < 0m) throw new ArgumentOutOfRangeException(nameof(threshold), "El umbral no puede ser negativo.");
        if (shortStopShare is < 0m or >= 0.5m)
            throw new ArgumentOutOfRangeException(nameof(shortStopShare), "La parada corta va de 0 a 0,5.");

        var (available, unavailable, invalid) = Prepare(cart, quotes);
        var chains = Enum.GetValues<Chain>();

        var candidates = new List<PurchaseOption>();
        if (available.Count > 0)
        {
            for (var mask = 1; mask < 1 << chains.Length; mask++)
            {
                var subset = chains.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                var option = Build(subset, available, shortStopShare);
                if (option.StoreCount > 0) candidates.Add(option);   // una cadena que no vende nada de la lista no es opción
            }
        }

        // Misma compra por dos caminos (p. ej. "COTO + lo que falta en Changomás" y el par COTO + Changomás cuando a
        // Changomás solo va lo que falta): se deja la completa; si ninguna lo es, la de menos supermercados.
        var unique = candidates
            .GroupBy(FullKey)
            .Select(g => g.OrderBy(o => o.IsComplete ? 0 : 1).ThenBy(o => o.StoreCount).First())
            .ToList();

        // Tres supermercados: solo si mejora a todas las opciones completas de uno o dos.
        var bestUpToTwo = unique.Where(o => o.IsComplete && o.StoreCount <= 2).Select(o => (decimal?)o.FullTotal).Min();
        unique.RemoveAll(o => o.StoreCount >= 3 && bestUpToTwo is { } b && o.FullTotal >= b);

        var ordered = unique
            .OrderBy(o => o.FullTotal)
            .ThenBy(o => o.Effort)
            .ThenBy(o => o.IsComplete ? 0 : 1)
            .ThenBy(o => string.Join(",", o.Stores.Select(s => (int)s)))
            .ToList();

        var cheapest = ordered.FirstOrDefault();
        var eligible = ordered.Where(o => o.IsComplete || o.MissingShare <= shortStopShare).ToList();
        PurchaseOption? recommended = null;
        if (cheapest is not null && eligible.Count > 0)
        {
            var limit = cheapest.FullTotal * (1m + threshold);
            recommended = eligible
                .Where(o => o.FullTotal <= limit)
                .OrderBy(o => o.Effort)
                .ThenBy(o => o.FullTotal)
                .ThenBy(o => o.MainStores.Count + o.ShortStops.Count)
                .FirstOrDefault()
                ?? eligible.First();
        }

        return new PurchaseOptionsResult(
            ordered,
            recommended,
            cheapest,
            ordered.FirstOrDefault(o => o.IsComplete && o.StoreCount == 1),
            threshold,
            shortStopShare,
            unavailable,
            invalid);
    }

    private static PurchaseOption Build(IReadOnlyList<Chain> subset, IReadOnlyList<Item> items, decimal shortStopShare)
    {
        var lines = new List<LineResult>();
        var complement = new List<LineResult>();
        var missing = new List<string>();
        foreach (var item in items)
        {
            if (Cheapest(item, subset) is { } inSubset)
            {
                lines.Add(new LineResult(item.Ean, item.Quantity, inSubset.Chain, inSubset.Price));
            }
            else
            {
                missing.Add(item.Ean);
                var anywhere = Cheapest(item, item.Prices.Keys.ToList())!.Value;
                complement.Add(new LineResult(item.Ean, item.Quantity, anywhere.Chain, anywhere.Price));
            }
        }

        var plan = Group(lines);
        var complementPlan = Group(complement);

        // Peso de cada supermercado en la compra entera (lo propio + lo que se completa ahí).
        var full = lines.Concat(complement).ToList();
        var fullTotal = full.Sum(l => l.Total);
        var shares = full
            .GroupBy(l => l.Chain)
            .ToDictionary(g => g.Key, g => fullTotal > 0m ? g.Sum(l => l.Total) / fullTotal : 0m);
        var main = shares.Where(s => s.Value > shortStopShare).Select(s => s.Key).OrderBy(c => (int)c).ToList();
        var shortStops = shares.Where(s => s.Value <= shortStopShare).Select(s => s.Key).OrderBy(c => (int)c).ToList();
        if (main.Count == 0 && shortStops.Count > 0)
        {
            // Imposible con hasta 3 cadenas y umbral < 1/3, pero por las dudas el más grande es el principal.
            var biggest = shares.MaxBy(s => s.Value).Key;
            main.Add(biggest);
            shortStops.Remove(biggest);
        }

        return new PurchaseOption(
            plan.Keys.ToList(),
            lines.Sum(l => l.Total),
            plan,
            missing,
            complementPlan,
            main,
            shortStops,
            main.Count + 0.5m * shortStops.Count);
    }

    private static (Chain Chain, decimal Price)? Cheapest(Item item, IEnumerable<Chain> chains) =>
        chains
            .Where(item.Prices.ContainsKey)
            .Select(c => (Chain: c, Price: item.Prices[c]))
            .OrderBy(p => p.Price)
            .ThenBy(p => (int)p.Chain)
            .Cast<(Chain Chain, decimal Price)?>()
            .FirstOrDefault();

    private static IReadOnlyDictionary<Chain, IReadOnlyList<LineResult>> Group(IEnumerable<LineResult> lines) =>
        lines
            .GroupBy(l => l.Chain)
            .OrderBy(g => (int)g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LineResult>)g.ToList());

    /// <summary>Identidad de una compra: qué producto termina en qué supermercado (incluido lo que se completa).</summary>
    private static string FullKey(PurchaseOption o) =>
        string.Join(";", o.Plan.Concat(o.Complement)
            .SelectMany(kv => kv.Value.Select(l => $"{l.Ean}@{(int)kv.Key}"))
            .Order(StringComparer.Ordinal));

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
