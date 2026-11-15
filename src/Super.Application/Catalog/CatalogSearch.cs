using System.Globalization;
using System.Text;
using Super.Domain;

namespace Super.Application.Catalog;

public sealed record CatalogEntry(string Ean, string Description, string Brand, string Presentation);

/// <summary>
/// Búsqueda en memoria sobre el catálogo (corre en el celular).
/// Texto: todas las palabras deben aparecer en la descripción o la marca, sin distinguir mayúsculas ni acentos.
/// Números: busca por código (el EAN tal como lo lee la cámara, o parte de él).
/// </summary>
public sealed class CatalogSearch
{
    private readonly CatalogEntry[] _entries;
    private readonly string[] _folded;
    private readonly Dictionary<string, CatalogEntry> _byEan;

    public CatalogSearch(IEnumerable<CatalogEntry> entries)
    {
        _entries = entries.ToArray();
        _folded = _entries.Select(e => Fold(e.Description + " " + e.Brand)).ToArray();
        _byEan = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        foreach (var e in _entries) _byEan.TryAdd(e.Ean, e);
    }

    public int Count => _entries.Length;

    /// <summary>Producto por código de barras (acepta EAN-8, EAN-13 o GTIN-14 sin normalizar).</summary>
    public CatalogEntry? Find(string? code) =>
        Ean.Normalize(code) is { } ean && _byEan.TryGetValue(ean, out var entry) ? entry : null;

    public IReadOnlyList<CatalogEntry> Search(string? query, int limit = 20)
    {
        var q = (query ?? "").Trim();
        if (q.Length < 2 || limit <= 0) return [];

        if (q.All(char.IsAsciiDigit))
        {
            var exact = Find(q);
            var partial = _entries.Where(e => e.Ean.Contains(q, StringComparison.Ordinal) && e != exact);
            return (exact is null ? partial : partial.Prepend(exact)).Take(limit).ToList();
        }

        var words = Fold(q).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6).ToArray();
        var hits = new List<int>();
        for (var i = 0; i < _folded.Length; i++)
        {
            var text = _folded[i];
            var all = true;
            foreach (var w in words)
            {
                if (!text.Contains(w, StringComparison.Ordinal)) { all = false; break; }
            }
            if (all) hits.Add(i);
        }

        return hits
            .OrderBy(i => _entries[i].Description.Length)
            .ThenBy(i => _entries[i].Description, StringComparer.Ordinal)
            .Take(limit)
            .Select(i => _entries[i])
            .ToList();
    }

    /// <summary>Mayúsculas sin acentos ("Azúcar" → "AZUCAR"); la ñ queda como N.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString();
    }
}
