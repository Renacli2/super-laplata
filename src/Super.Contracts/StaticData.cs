using System.Text.Json.Serialization;

namespace Super.Contracts;

// Archivos estáticos que genera Super.Export una vez por día y que la app descarga:
//   data/version.json   fecha de los precios (chico, para saber si hay datos nuevos)
//   data/catalog.json   productos para buscar (lo usan todos)
//   data/prices.json    precios por cadena (solo el administrador, al comparar)
// Los nombres de propiedad son de una letra para achicar los archivos (~43 mil productos).

public sealed record DataVersionDto(
    [property: JsonPropertyName("date")] DateOnly PriceDate,
    [property: JsonPropertyName("generated")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("products")] int Products);

public sealed record CatalogProductDto(
    [property: JsonPropertyName("e")] string Ean,
    [property: JsonPropertyName("d")] string Description,
    [property: JsonPropertyName("b")] string Brand,
    [property: JsonPropertyName("p")] string Presentation);

public sealed record CatalogFileDto(
    [property: JsonPropertyName("date")] DateOnly PriceDate,
    [property: JsonPropertyName("products")] IReadOnlyList<CatalogProductDto> Products);

/// <param name="Id">Nombre del enum Chain ("Carrefour", "Changomas", "Coto").</param>
public sealed record ChainInfoDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("date")] DateOnly PriceDate);

/// <summary>Precios de un EAN, en el mismo orden que <see cref="PricesFileDto.Chains"/> (null = no lo vende).</summary>
public sealed record PriceRowDto(
    [property: JsonPropertyName("e")] string Ean,
    [property: JsonPropertyName("p")] IReadOnlyList<decimal?> Prices);

public sealed record PricesFileDto(
    [property: JsonPropertyName("date")] DateOnly PriceDate,
    [property: JsonPropertyName("chains")] IReadOnlyList<ChainInfoDto> Chains,
    [property: JsonPropertyName("prices")] IReadOnlyList<PriceRowDto> Prices);
