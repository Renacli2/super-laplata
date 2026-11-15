namespace Super.Application.Comparison;

/// <summary>Origen de precios para el motor de comparación.</summary>
public interface IPriceQuoteSource
{
    /// <summary>
    /// Precios vigentes (último día importado de cada cadena) de los EAN pedidos.
    /// Los códigos se normalizan; los que no son EAN válidos se ignoran.
    /// </summary>
    Task<IReadOnlyList<PriceQuote>> GetQuotesAsync(IEnumerable<string> eans, CancellationToken ct = default);
}
