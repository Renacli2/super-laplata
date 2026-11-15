using System.Text;
using Super.Domain;

namespace Super.Application.Orders;

public sealed record OrderItem(string Ean, int Quantity);

public sealed record Order(string From, IReadOnlyList<OrderItem> Items);

/// <summary>
/// Pedido de un miembro codificado en la dirección, para mandarlo por WhatsApp sin servidor:
/// <c>pedido?de=Ana&amp;i=7790360981042x2-40084107x1</c>. Solo viajan códigos y cantidades.
/// </summary>
public static class OrderLink
{
    public const int MaxItems = 200;
    public const int MaxQuantity = 999;
    public const int MaxNameLength = 40;

    public static string Encode(Order order)
    {
        var items = string.Join("-", Merge(order.Items).Select(i => $"{Shorten(i.Ean)}x{i.Quantity}"));
        var name = CleanName(order.From);
        return name.Length == 0
            ? $"pedido?i={items}"
            : $"pedido?de={Uri.EscapeDataString(name)}&i={items}";
    }

    /// <summary>Lee los parámetros <c>de</c> e <c>i</c>; ignora lo inválido y devuelve null si no queda ningún ítem.</summary>
    public static Order? Decode(string? from, string? items)
    {
        if (string.IsNullOrWhiteSpace(items)) return null;

        var parsed = new List<OrderItem>();
        foreach (var part in items.Split('-', StringSplitOptions.RemoveEmptyEntries).Take(MaxItems))
        {
            var pieces = part.Split('x', 2);
            var ean = Ean.Normalize(pieces[0]);
            if (ean is null) continue;
            var qty = pieces.Length == 2 && int.TryParse(pieces[1], out var q) ? q : 1;
            if (qty < 1) continue;
            parsed.Add(new OrderItem(ean, Math.Min(qty, MaxQuantity)));
        }

        var merged = Merge(parsed);
        return merged.Count == 0 ? null : new Order(CleanName(from), merged);
    }

    private static List<OrderItem> Merge(IEnumerable<OrderItem> items) =>
        items
            .Select(i => (Ean: Ean.Normalize(i.Ean), i.Quantity))
            .Where(i => i.Ean is not null && i.Quantity > 0)
            .GroupBy(i => i.Ean!)
            .Select(g => new OrderItem(g.Key, Math.Min(g.Sum(i => i.Quantity), MaxQuantity)))
            .Take(MaxItems)
            .ToList();

    /// <summary>Saca los ceros de relleno (0000040084107 → 40084107) sin bajar de 8 dígitos.</summary>
    private static string Shorten(string ean)
    {
        var trimmed = ean.TrimStart('0');
        return trimmed.Length >= 8 ? trimmed : ean[^8..];
    }

    private static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        foreach (var c in (name ?? "").Trim())
        {
            if (!char.IsControl(c)) sb.Append(c);
            if (sb.Length == MaxNameLength) break;
        }
        return sb.ToString().Trim();
    }
}
