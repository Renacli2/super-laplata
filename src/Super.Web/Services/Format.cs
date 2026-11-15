using System.Globalization;

namespace Super.Web.Services;

public static class Format
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>$ 53.543 (sin centavos si son cero) o $ 2.769,99.</summary>
    public static string Money(decimal value) =>
        "$ " + (value == decimal.Truncate(value) ? value.ToString("N0", Ar) : value.ToString("N2", Ar));

    /// <summary>Solo el número, para los precios grandes del cartel.</summary>
    public static string Amount(decimal value) => value.ToString("N0", Ar);

    /// <summary>Centavos ",99" para mostrarlos chicos al lado del entero; vacío si son cero.</summary>
    public static string Cents(decimal value)
    {
        var cents = (int)Math.Round((value - decimal.Truncate(value)) * 100m);
        return cents == 0 ? "" : "," + cents.ToString("00", Ar);
    }

    public static string Percent(decimal ratio) => (ratio * 100m).ToString("0.#", Ar) + " %";

    public static string Date(DateOnly date) => date.ToString("d 'de' MMMM", Ar);

    public static string Products(int count) => count == 1 ? "1 producto" : $"{count} productos";

    /// <summary>Clase CSS de la cadena (color de dato, no de marca).</summary>
    public static string ChainClass(string chain) => "chain-" + chain.ToLowerInvariant();
}
