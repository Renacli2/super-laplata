using System.Globalization;
using System.Text;
using Super.Application.Comparison;
using Super.Domain;

namespace Super.Infrastructure.Sepa;

/// <summary>Filtro de sucursales: provincia de Buenos Aires (AR-B) y localidad que empieza con "La Plata".</summary>
public sealed class LaPlataBranchFilter
{
    public bool IsMatch(SepaRow branch) =>
        branch["sucursales_provincia"].Equals("AR-B", StringComparison.OrdinalIgnoreCase)
        && Normalize(branch["sucursales_localidad"]).StartsWith("la plata", StringComparison.Ordinal);

    internal static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Límites de plausibilidad de precios (ARS). Descarta, por ejemplo, el $0,10 visto en Changomás.</summary>
public sealed record PriceValidator(decimal MinPrice = 1m, decimal MaxPrice = 50_000_000m)
{
    public bool IsPlausible(decimal price) => price >= MinPrice && price <= MaxPrice;
}

public sealed record ImportedPrice(
    Chain Chain,
    string BannerId,
    string BranchId,
    string Ean,
    string Description,
    string Brand,
    decimal ListPrice,
    decimal? ReferencePrice,
    decimal? PresentationQuantity,
    string PresentationUnit,
    decimal? PromoPrice);

public sealed record ImportStats(
    int RowsRead,
    int RowsInZone,
    int SkippedNoEan,
    int SkippedInvalidEan,
    int SkippedBadPrice,
    int Accepted);

/// <summary>Sucursal de La Plata tal como figura en sucursales.csv.</summary>
public sealed record ImportedBranch(
    Chain Chain,
    string BannerId,
    string BranchId,
    string Name,
    string Street,
    string Number,
    string Locality,
    decimal? Latitude,
    decimal? Longitude);

public sealed record ImportResult(
    IReadOnlyList<ImportedPrice> Prices,
    ImportStats Stats,
    IReadOnlyList<ImportedBranch> Branches);

public sealed class SepaImporter
{
    private readonly LaPlataBranchFilter _filter;
    private readonly PriceValidator _validator;

    public SepaImporter(LaPlataBranchFilter? filter = null, PriceValidator? validator = null)
    {
        _filter = filter ?? new LaPlataBranchFilter();
        _validator = validator ?? new PriceValidator();
    }

    /// <summary>
    /// Importa los precios de una cadena para las sucursales de La Plata.
    /// En SEPA el EAN real está en <c>id_producto</c>; <c>productos_ean</c> es solo un indicador (1 = es EAN).
    /// </summary>
    public ImportResult Import(Chain chain, TextReader sucursales, TextReader productos)
    {
        var branches = new HashSet<(string Banner, string Branch)>();
        var branchInfo = new List<ImportedBranch>();
        foreach (var row in SepaCsv.Read(sucursales))
        {
            if (!_filter.IsMatch(row)) continue;
            if (!branches.Add((row["id_bandera"], row["id_sucursal"]))) continue;
            branchInfo.Add(new ImportedBranch(
                chain,
                row["id_bandera"],
                row["id_sucursal"],
                row["sucursales_nombre"],
                row["sucursales_calle"],
                row["sucursales_numero"],
                row["sucursales_localidad"],
                row.Decimal("sucursales_latitud"),
                row.Decimal("sucursales_longitud")));
        }

        int read = 0, inZone = 0, noEan = 0, invalidEan = 0, badPrice = 0;
        var prices = new List<ImportedPrice>();

        foreach (var row in SepaCsv.Read(productos))
        {
            read++;
            if (!branches.Contains((row["id_bandera"], row["id_sucursal"]))) continue;
            inZone++;

            if (row["productos_ean"] != "1") { noEan++; continue; }

            var ean = Ean.Normalize(row["id_producto"]);
            if (ean is null || Ean.IsVariableWeight(ean)) { invalidEan++; continue; }

            var list = row.Decimal("productos_precio_lista");
            if (list is null || !_validator.IsPlausible(list.Value)) { badPrice++; continue; }

            prices.Add(new ImportedPrice(
                chain,
                row["id_bandera"],
                row["id_sucursal"],
                ean,
                row["productos_descripcion"],
                row["productos_marca"],
                list.Value,
                row.Decimal("productos_precio_referencia"),
                row.Decimal("productos_cantidad_presentacion"),
                row["productos_unidad_medida_presentacion"],
                row.Decimal("productos_precio_unitario_promo1")));
        }

        return new ImportResult(
            prices,
            new ImportStats(read, inZone, noEan, invalidEan, badPrice, prices.Count),
            branchInfo);
    }

    /// <summary>
    /// Reduce los precios por sucursal a uno por (cadena, EAN): el más barato entre las sucursales de La Plata.
    /// Usa el precio de lista; las promociones (PromoPrice) todavía no se aplican.
    /// </summary>
    public static IEnumerable<PriceQuote> CheapestPerChain(IEnumerable<ImportedPrice> prices) =>
        prices
            .GroupBy(p => (p.Chain, p.Ean))
            .Select(g => new PriceQuote(g.Key.Chain, g.Key.Ean, g.Min(p => p.ListPrice)));
}
