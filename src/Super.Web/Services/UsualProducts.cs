using Super.Application.Catalog;
using Super.Domain;

namespace Super.Web.Services;

/// <summary>Un producto habitual para mostrar: marcado con la estrella o sugerido por las compras confirmadas.</summary>
/// <param name="Ean">EAN normalizado.</param>
/// <param name="Quantity">La de la estrella si está marcado; si es sugerido, la de la última compra.</param>
/// <param name="IsStarred">Lo marcó el usuario. Si no, es una sugerencia aprendida de las compras.</param>
/// <param name="TimesBought">En cuántas compras confirmadas estuvo.</param>
public sealed record UsualProduct(
    string Ean, string Description, string Brand, string Presentation, int Quantity, bool IsStarred, int TimesBought, DateTimeOffset? LastBought)
{
    public CatalogEntry ToEntry() => new(Ean, Description, Brand, Presentation);
}

/// <summary>
/// Junta los habituales marcados con lo que se compra seguido. Los marcados van primero (la estrella siempre gana);
/// después, como sugerencia, lo que estuvo en al menos <see cref="MinTimesForSuggestion"/> compras confirmadas.
/// Dentro de cada grupo, lo más comprado primero.
/// </summary>
public static class UsualProducts
{
    public const int MinTimesForSuggestion = 2;

    /// <param name="inList">EAN (normalizados) que ya están en la lista: no se muestran.</param>
    public static List<UsualProduct> Build(
        IReadOnlyList<FavoriteItem> favorites, IReadOnlyList<Purchase> purchases, IReadOnlySet<string> inList)
    {
        var usage = Usage(purchases);

        var starred = favorites
            .Where(f => !inList.Contains(f.Ean))
            .Select(f =>
            {
                var u = usage.GetValueOrDefault(f.Ean);
                return new UsualProduct(f.Ean, f.Description, f.Brand, f.Presentation, f.Quantity, true, u?.Times ?? 0, u?.Last);
            });

        var starredEans = favorites.Select(f => f.Ean).ToHashSet(StringComparer.Ordinal);
        var suggested = usage.Values
            .Where(u => u.Times >= MinTimesForSuggestion && !starredEans.Contains(u.Ean) && !inList.Contains(u.Ean))
            .Select(u => new UsualProduct(u.Ean, u.Description, "", "", u.LastQuantity, false, u.Times, u.Last));

        return Order(starred).Concat(Order(suggested)).ToList();
    }

    /// <summary>EAN que la búsqueda pone primero: los marcados y los sugeridos.</summary>
    public static HashSet<string> PreferredEans(IReadOnlyList<FavoriteItem> favorites, IReadOnlyList<Purchase> purchases) =>
        Build(favorites, purchases, new HashSet<string>()).Select(u => u.Ean).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<UsualProduct> Order(IEnumerable<UsualProduct> items) => items
        .OrderByDescending(u => u.TimesBought)
        .ThenByDescending(u => u.LastBought)
        .ThenBy(u => CatalogSearch.Fold(u.Description), StringComparer.Ordinal)
        .ThenBy(u => u.Ean, StringComparer.Ordinal);

    private sealed record Use(string Ean, string Description, int Times, DateTimeOffset Last, int LastQuantity);

    /// <summary>Por producto: en cuántas compras estuvo, cuándo fue la última y qué cantidad se llevó esa vez.</summary>
    private static Dictionary<string, Use> Usage(IReadOnlyList<Purchase> purchases)
    {
        var usage = new Dictionary<string, Use>(StringComparer.Ordinal);
        foreach (var purchase in purchases.OrderBy(p => p.At))
        {
            // Un producto cuenta una vez por compra, aunque aparezca repetido.
            foreach (var item in purchase.Items.DistinctBy(i => Key(i.Ean)))
            {
                var ean = Key(item.Ean);
                var times = usage.TryGetValue(ean, out var prev) ? prev.Times + 1 : 1;
                usage[ean] = new Use(ean, item.Description, times, purchase.At, Math.Max(item.Quantity, 1));
            }
        }
        return usage;
    }

    private static string Key(string ean) => Ean.Normalize(ean) ?? ean;
}
