namespace Super.Domain;

/// <summary>Cadenas incluidas en la comparación.</summary>
public enum Chain
{
    Carrefour = 1,
    Changomas = 2,
    Coto = 3
}

public static class ChainExtensions
{
    public static string DisplayName(this Chain chain) => chain switch
    {
        Chain.Carrefour => "Carrefour",
        Chain.Changomas => "Changomás",
        Chain.Coto => "COTO",
        _ => chain.ToString()
    };

    /// <summary>
    /// Mapea el id_comercio de SEPA a la cadena (valores observados en el dump del 13/11/2026:
    /// 10 = Carrefour (INC S.A.), 11 = Changomás (Dorinka SRL), 12 = COTO CICSA).
    /// </summary>
    public static bool TryFromSepaCommerceId(string? idComercio, out Chain chain)
    {
        switch (idComercio?.Trim())
        {
            case "10": chain = Chain.Carrefour; return true;
            case "11": chain = Chain.Changomas; return true;
            case "12": chain = Chain.Coto; return true;
            default: chain = default; return false;
        }
    }
}
