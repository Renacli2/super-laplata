using System.Text.RegularExpressions;
using Super.Domain;

namespace Super.Application.Catalog;

/// <summary>Unidad para comparar tamaños: gramos o mililitros.</summary>
public enum SizeUnit { Grams, Milliliters }

/// <summary>Tamaño del envase ("X 1.5 L" → 1500 ml).</summary>
public sealed record ProductSize(decimal Quantity, SizeUnit Unit)
{
    /// <summary>"kg" o "l": la unidad del precio por unidad que se muestra.</summary>
    public string PerUnitLabel => Unit == SizeUnit.Grams ? "kg" : "l";

    /// <summary>Precio por kilo o por litro.</summary>
    public decimal PerUnitPrice(decimal price) => price / Quantity * 1000m;
}

/// <summary>
/// Lo que la app sabe de un producto a partir de su descripción, porque SEPA no informa el tipo de producto
/// y el tamaño viene distinto según la cadena ("1 uni", "EA", "500 grm"): el tipo (primeras palabras),
/// el tamaño y las palabras de variante (light, zero, sabores…).
/// </summary>
public sealed partial record ProductTraits(IReadOnlyList<string> TypeWords, IReadOnlySet<string> Words, ProductSize? Size)
{
    /// <summary>Cuántas palabras del tipo tienen que coincidir ("YERBA MATE SUAVE").</summary>
    public const int TypeLength = 3;

    // Palabras que no dicen qué es el producto: envases, conectores, abreviaturas.
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "X", "DE", "DEL", "EN", "CON", "C", "SIN", "S", "LA", "EL", "LOS", "LAS", "Y", "P", "PARA", "PAQ", "PAQUETE", "BOT",
        "BOTELLA", "BOLSA", "BOL", "PET", "LATA", "CJA", "CAJA", "POTE", "POX", "POTX", "DP", "DOYPACK", "TETRA", "BRIK",
        "ESTUCHE", "SACHET", "SCH", "FRASCO", "FCO", "VIDRIO", "UNI", "UN", "UNIDADES", "UDS",
    };

    /// <summary>
    /// Palabras que cambian el producto: si el equivalente tiene una de estas y el original no, no se sugiere
    /// (no se cambia una leche entera por una descremada ni una mermelada por una light).
    /// </summary>
    public static readonly IReadOnlySet<string> Variants = new HashSet<string>(StringComparer.Ordinal)
    {
        "LIGHT", "ZERO", "CERO", "DIET", "DESCREMADA", "DESCREMADO", "PARCIALMENTE", "DESLACTOSADA", "DESLACTOSADO",
        "DESMENUZADO", "PROCESADO", "SABORIZADA", "SABORIZADO", "SODIO", "REPOSTERO", "INTEGRAL", "ORGANICO", "ORGANICA",
        "LIMON", "NARANJA", "POMELO", "MANZANA", "FRUTILLA", "DURAZNO", "UVA", "VAINILLA", "CHOCOLATE", "FRUTOS",
    };

    private static readonly Dictionary<string, (SizeUnit Unit, decimal Factor)> Units = new(StringComparer.Ordinal)
    {
        ["G"] = (SizeUnit.Grams, 1), ["GR"] = (SizeUnit.Grams, 1), ["GRS"] = (SizeUnit.Grams, 1), ["GRM"] = (SizeUnit.Grams, 1),
        ["GRAMOS"] = (SizeUnit.Grams, 1), ["KG"] = (SizeUnit.Grams, 1000), ["KGM"] = (SizeUnit.Grams, 1000), ["KGS"] = (SizeUnit.Grams, 1000),
        ["ML"] = (SizeUnit.Milliliters, 1), ["CC"] = (SizeUnit.Milliliters, 1), ["CM3"] = (SizeUnit.Milliliters, 1), ["CMQ"] = (SizeUnit.Milliliters, 1),
        ["L"] = (SizeUnit.Milliliters, 1000), ["LT"] = (SizeUnit.Milliliters, 1000), ["LTS"] = (SizeUnit.Milliliters, 1000),
        ["LTR"] = (SizeUnit.Milliliters, 1000), ["LITRO"] = (SizeUnit.Milliliters, 1000), ["LITROS"] = (SizeUnit.Milliliters, 1000),
    };

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(KGM|KGS|KG|GRAMOS|GRM|GRS|GR|G|LITROS|LITRO|LTR|LTS|LT|L|ML|CC|CM3|CMQ)\b")]
    private static partial Regex SizePattern();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?[A-Z0-9]*")]
    private static partial Regex NumberToken();

    public static ProductTraits From(string description, string brand)
    {
        var text = CatalogSearch.Fold(description);
        var words = Tokens(NumberToken().Replace(text, " "));
        var brandWords = Tokens(CatalogSearch.Fold(brand));

        // El tipo son las palabras antes de la marca ("LECHE ENTERA" en "LECHE ENTERA TREGAR TETRA").
        // Si la descripción empieza con la marca ("CHECK ARROZ LARGO FINO"), las que vienen después.
        var start = brandWords.Count == 0 ? -1 : words.IndexOf(brandWords[0]);
        List<string> type;
        if (start > 0)
        {
            type = words.Take(start).ToList();
        }
        else if (start == 0)
        {
            var skip = 0;
            while (skip < words.Count && brandWords.Contains(words[skip])) skip++;
            type = words.Skip(skip).ToList();
        }
        else
        {
            type = words;
        }

        return new ProductTraits(type.Take(TypeLength).ToList(), words.Concat(brandWords).ToHashSet(StringComparer.Ordinal), ParseSize(text));
    }

    /// <summary>Tamaño a partir de la descripción (el último "número + unidad" que aparece).</summary>
    public static ProductSize? ParseSize(string foldedDescription)
    {
        var matches = SizePattern().Matches(foldedDescription);
        if (matches.Count == 0) return null;
        var m = matches[^1];
        if (!decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number <= 0)
            return null;
        var (unit, factor) = Units[m.Groups[2].Value];
        return new ProductSize(number * factor, unit);
    }

    private static List<string> Tokens(string folded) =>
        folded.Split(NonLetters, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !Noise.Contains(w))
            .ToList();

    private static readonly char[] NonLetters =
        " .,;:-_/()[]+*'\"!¡?¿%&#0123456789".ToCharArray();
}

/// <summary>Un producto equivalente más barato.</summary>
/// <param name="Saving">Cuánto más barato por kilo o litro (0,22 = 22 %).</param>
public sealed record Equivalent(CatalogEntry Product, ProductSize Size, decimal Price, IReadOnlyList<Chain> Chains, decimal PerUnitPrice, decimal Saving);

/// <summary>Precio más bajo de un producto y en qué cadenas está a ese precio.</summary>
public sealed record BestPrice(decimal Price, IReadOnlyList<Chain> Chains);

/// <summary>
/// Busca, para un producto de la lista, otros del mismo tipo y tamaño parecido que salgan menos por kilo o litro.
/// No cambia nada: solo sugiere, y la persona decide.
/// </summary>
public sealed class EquivalentFinder
{
    /// <summary>Diferencia de tamaño aceptada (un dulce de leche de 400 g se compara con uno de 360 g, no con uno de 1 kg).</summary>
    public const decimal SizeTolerance = 0.20m;

    /// <summary>Ahorro mínimo por kilo o litro para sugerirlo (para no molestar por diferencias chicas).</summary>
    public const decimal MinSaving = 0.05m;

    private readonly Dictionary<string, (CatalogEntry Entry, ProductTraits Traits, BestPrice Price)> _byEan = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byFirstWord = new(StringComparer.Ordinal);

    public EquivalentFinder(IEnumerable<CatalogEntry> catalog, IReadOnlyDictionary<string, BestPrice> prices)
    {
        foreach (var entry in catalog)
        {
            if (!prices.TryGetValue(entry.Ean, out var price)) continue;
            var traits = ProductTraits.From(entry.Description, entry.Brand);
            if (traits.Size is null || traits.TypeWords.Count == 0) continue;
            if (!_byEan.TryAdd(entry.Ean, (entry, traits, price))) continue;
            if (!_byFirstWord.TryGetValue(traits.TypeWords[0], out var list))
                _byFirstWord[traits.TypeWords[0]] = list = [];
            list.Add(entry.Ean);
        }
    }

    /// <summary>Hasta <paramref name="max"/> equivalentes más baratos, del más barato por kilo o litro al menos.</summary>
    public IReadOnlyList<Equivalent> Find(string ean, int max = 3)
    {
        if (!_byEan.TryGetValue(ean, out var me)) return [];
        var size = me.Traits.Size!;
        var mine = size.PerUnitPrice(me.Price.Price);

        if (!_byFirstWord.TryGetValue(me.Traits.TypeWords[0], out var candidates)) return [];
        var result = new List<Equivalent>();
        foreach (var other in candidates)
        {
            if (other == ean) continue;
            var c = _byEan[other];
            var s = c.Traits.Size!;
            if (s.Unit != size.Unit) continue;
            if (s.Quantity < size.Quantity * (1 - SizeTolerance) || s.Quantity > size.Quantity * (1 + SizeTolerance)) continue;
            if (!me.Traits.TypeWords.All(c.Traits.Words.Contains)) continue;
            if (c.Traits.Words.Any(w => ProductTraits.Variants.Contains(w) && !me.Traits.Words.Contains(w))) continue;

            var perUnit = s.PerUnitPrice(c.Price.Price);
            var saving = 1 - perUnit / mine;
            if (saving < MinSaving) continue;
            result.Add(new Equivalent(c.Entry, s, c.Price.Price, c.Price.Chains, perUnit, saving));
        }

        return result.OrderBy(e => e.PerUnitPrice).ThenBy(e => e.Product.Description, StringComparer.Ordinal).Take(max).ToList();
    }

    /// <summary>Tamaño del producto, si la descripción lo dice.</summary>
    public ProductSize? SizeOf(string ean) => _byEan.TryGetValue(ean, out var me) ? me.Traits.Size : null;

    /// <summary>
    /// Cuánto se ahorra comprando el mejor equivalente en vez del original, para la cantidad pedida
    /// (por kilo o litro, llevado al tamaño del original). 0 si no hay equivalente.
    /// </summary>
    public decimal SavingFor(string ean, int quantity)
    {
        if (!_byEan.TryGetValue(ean, out var me)) return 0;
        var best = Find(ean, 1);
        if (best.Count == 0) return 0;
        var size = me.Traits.Size!;
        return (size.PerUnitPrice(me.Price.Price) - best[0].PerUnitPrice) * size.Quantity / 1000m * quantity;
    }
}
