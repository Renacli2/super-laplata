namespace Super.Contracts;

// Contratos JSON entre Super.Api y la app (Super.Web). Las cadenas viajan como texto
// ("Carrefour", "Changomas", "Coto") para que el cliente no dependa del dominio.

/// <summary>Precio vigente de un producto en una cadena (mínimo entre sus sucursales de La Plata).</summary>
public sealed record ChainPriceDto(string Chain, string ChainName, decimal Price);

public sealed record ProductDto(
    string Ean,
    string Description,
    string Brand,
    string Presentation,
    IReadOnlyList<ChainPriceDto> Prices);

public sealed record CompareItem(string Ean, int Quantity);

public sealed record CompareRequest(IReadOnlyList<CompareItem> Items, decimal? Threshold = null);

public sealed record ChainTotalDto(string Chain, string ChainName, int CoveredItems, int TotalItems, decimal? Total);

public sealed record PlanLineDto(string Ean, string Description, int Quantity, decimal UnitPrice, decimal Total);

public sealed record PlanGroupDto(string Chain, string ChainName, decimal Subtotal, IReadOnlyList<PlanLineDto> Lines);

public sealed record CompareResponse(
    decimal SplitTotal,
    string? BestSingleChain,
    string? BestSingleChainName,
    decimal? BestSingleTotal,
    decimal? Gap,
    decimal Threshold,
    bool RecommendSingleStore,
    IReadOnlyList<ChainTotalDto> Chains,
    IReadOnlyList<PlanGroupDto> Plan,
    IReadOnlyList<string> UnavailableEans,
    IReadOnlyList<string> InvalidCodes);

public sealed record ChainImportDto(string Chain, string ChainName, DateOnly PriceDate, int Products);

public sealed record StatusDto(IReadOnlyList<ChainImportDto> Chains, int Products);
