namespace Super.Application.Comparison;

/// <summary>
/// Cuánto va de la compra en una opción: lo que ya está en el changuito y lo que falta, en plata y productos.
/// Los precios son los de SEPA, así que el ticket puede diferir un poco.
/// </summary>
public sealed record ShoppingProgress(decimal PickedTotal, decimal RemainingTotal, int PickedCount, int RemainingCount)
{
    public decimal Total => PickedTotal + RemainingTotal;
    public int Count => PickedCount + RemainingCount;
    public bool IsDone => Count > 0 && RemainingCount == 0;

    /// <summary>Avance sobre un grupo de líneas del plan, según los EAN que ya están en el changuito.</summary>
    public static ShoppingProgress For(IEnumerable<LineResult> lines, ISet<string> inCart)
    {
        decimal picked = 0, remaining = 0;
        int pickedCount = 0, remainingCount = 0;
        foreach (var line in lines)
        {
            if (inCart.Contains(line.Ean))
            {
                picked += line.Total;
                pickedCount++;
            }
            else
            {
                remaining += line.Total;
                remainingCount++;
            }
        }
        return new ShoppingProgress(picked, remaining, pickedCount, remainingCount);
    }

    /// <summary>Avance de toda la opción: sus supermercados y donde se compra lo que falta.</summary>
    public static ShoppingProgress For(PurchaseOption option, ISet<string> inCart) =>
        For(option.Plan.Values.SelectMany(l => l).Concat(option.Complement.Values.SelectMany(l => l)), inCart);
}
