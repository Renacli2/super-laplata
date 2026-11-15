using Super.Domain;

namespace Super.Application.Comparison;

/// <summary>Una línea del carrito: código de barras y cantidad.</summary>
public sealed record CartLine(string Ean, int Quantity);

/// <summary>Precio de un producto en una cadena (ya reducido a un único valor por cadena y EAN).</summary>
public sealed record PriceQuote(Chain Chain, string Ean, decimal UnitPrice);

/// <summary>Línea del plan de compra: qué comprar, dónde y a qué precio.</summary>
public sealed record LineResult(string Ean, int Quantity, Chain Chain, decimal UnitPrice)
{
    public decimal Total => UnitPrice * Quantity;
}

/// <summary>
/// Resumen por cadena. <see cref="Total"/> solo tiene valor si la cadena tiene TODOS los productos
/// disponibles del carrito (los que existen en al menos una cadena).
/// </summary>
public sealed record ChainSummary(Chain Chain, int CoveredItems, int TotalItems, decimal? Total)
{
    public bool IsComplete => Total.HasValue;
}

/// <summary>Resultado de comparar un carrito.</summary>
/// <param name="SplitTotal">Suma de los mínimos por ítem (compra repartida óptima).</param>
/// <param name="BestSingleChain">Mejor cadena única con cobertura completa; null si ninguna la tiene.</param>
/// <param name="BestSingleTotal">Total del carrito en esa cadena.</param>
/// <param name="Gap">(mejorTotalÚnico − totalRepartido) / totalRepartido; null si no hay cadena única posible.</param>
/// <param name="Threshold">Umbral aplicado.</param>
/// <param name="RecommendSingleStore">true = se propone una sola cadena; false = una lista por cadena.</param>
/// <param name="Plan">Líneas de compra agrupadas por cadena.</param>
/// <param name="ChainSummaries">Cobertura y total por cadena.</param>
/// <param name="UnavailableEans">EAN válidos que no están en ninguna cadena (candidatos a ítem libre).</param>
/// <param name="InvalidCodes">Códigos que no son EAN comparables (inválidos o de peso variable).</param>
public sealed record ComparisonResult(
    decimal SplitTotal,
    Chain? BestSingleChain,
    decimal? BestSingleTotal,
    decimal? Gap,
    decimal Threshold,
    bool RecommendSingleStore,
    IReadOnlyDictionary<Chain, IReadOnlyList<LineResult>> Plan,
    IReadOnlyList<ChainSummary> ChainSummaries,
    IReadOnlyList<string> UnavailableEans,
    IReadOnlyList<string> InvalidCodes);
