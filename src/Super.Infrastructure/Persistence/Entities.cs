using Super.Domain;

namespace Super.Infrastructure.Persistence;

/// <summary>Producto identificado por su EAN canónico (ver <see cref="Ean.Normalize"/>).</summary>
public sealed class Product
{
    public required string Ean { get; set; }
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "";
    public decimal? PresentationQuantity { get; set; }
    public string PresentationUnit { get; set; } = "";
    public DateOnly FirstSeenOn { get; set; }
    public DateOnly LastSeenOn { get; set; }
}

/// <summary>Sucursal de La Plata. La clave natural en SEPA es (cadena, id_bandera, id_sucursal).</summary>
public sealed class Branch
{
    public int Id { get; set; }
    public Chain Chain { get; set; }
    public required string SepaBannerId { get; set; }
    public required string SepaBranchId { get; set; }
    public string Name { get; set; } = "";
    public string Street { get; set; } = "";
    public string Number { get; set; } = "";
    public string Locality { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateOnly LastSeenOn { get; set; }
}

/// <summary>Precio de un producto en una sucursal para el día del dump.</summary>
public sealed class BranchPrice
{
    public DateOnly PriceDate { get; set; }
    public int BranchId { get; set; }
    public required string Ean { get; set; }
    public decimal ListPrice { get; set; }
    public decimal? ReferencePrice { get; set; }
    public decimal? PromoPrice { get; set; }

    public Branch? Branch { get; set; }
    public Product? Product { get; set; }
}

/// <summary>
/// Precio de lista mínimo de un producto entre las sucursales de La Plata de una cadena, para un día.
/// Es la tabla que alimenta al motor de comparación.
/// </summary>
public sealed class ChainPrice
{
    public DateOnly PriceDate { get; set; }
    public Chain Chain { get; set; }
    public required string Ean { get; set; }
    public decimal MinListPrice { get; set; }
    public int BranchCount { get; set; }

    public Product? Product { get; set; }
}

/// <summary>Registro de cada ingesta (una por cadena y día; reimportar reemplaza la anterior).</summary>
public sealed class ImportRun
{
    public int Id { get; set; }
    public DateOnly PriceDate { get; set; }
    public Chain Chain { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public int RowsRead { get; set; }
    public int RowsInZone { get; set; }
    public int SkippedNoEan { get; set; }
    public int SkippedInvalidEan { get; set; }
    public int SkippedBadPrice { get; set; }
    public int Accepted { get; set; }
    public int Branches { get; set; }
    public int DistinctProducts { get; set; }
}
