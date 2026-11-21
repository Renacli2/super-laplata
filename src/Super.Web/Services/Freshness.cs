namespace Super.Web.Services;

/// <summary>Antigüedad de los precios publicados, para avisar si la PC no subió precios nuevos.</summary>
public static class Freshness
{
    /// <summary>Con más días que estos, los precios se consideran viejos.</summary>
    public const int StaleAfterDays = 2;

    public static int AgeInDays(DateOnly priceDate, DateOnly today) => Math.Max(0, today.DayNumber - priceDate.DayNumber);

    public static bool IsStale(DateOnly priceDate, DateOnly today) => AgeInDays(priceDate, today) > StaleAfterDays;

    /// <summary>"hoy", "ayer", "hace 4 días".</summary>
    public static string Ago(int days) => days switch
    {
        0 => "hoy",
        1 => "ayer",
        _ => $"hace {days} días",
    };

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);
}
