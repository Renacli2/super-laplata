using Microsoft.EntityFrameworkCore;
using Super.Api;
using Super.Application.Comparison;
using Super.Contracts;
using Super.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Conexión: ConnectionStrings:Super (appsettings, variable ConnectionStrings__Super o SUPER_DB)
// o appsettings.Local.json en la carpeta de la app o alguna superior (desarrollo).
if (builder.Configuration.GetConnectionString("Super") is null
    && LocalSettings.FindFile(builder.Environment.ContentRootPath) is { } localSettings)
{
    builder.Configuration.AddJsonFile(localSettings, optional: true);
}
var connectionString = builder.Configuration.GetConnectionString("Super")
    ?? Environment.GetEnvironmentVariable("SUPER_DB")
    ?? throw new InvalidOperationException(
        "Falta la conexión a PostgreSQL: definí ConnectionStrings__Super o creá appsettings.Local.json.");

builder.Services.AddDbContext<SuperDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
builder.Services.AddScoped<IPriceQuoteSource, EfPriceQuoteSource>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

// MapStaticAssets sirve la app Blazor (incluido _framework, con compresión y huellas en los nombres).
// No combinar con UseBlazorFrameworkFiles: reescribe las rutas comprimidas y rompe la descarga.
app.MapStaticAssets();

var api = app.MapGroup("/api");

api.MapGet("/status", (CatalogService catalog, CancellationToken ct) => catalog.StatusAsync(ct));

api.MapGet("/products", (string? q, int? limit, CatalogService catalog, CancellationToken ct) =>
    catalog.SearchAsync(q, limit ?? 20, ct));

api.MapGet("/products/{code}", async (string code, CatalogService catalog, CancellationToken ct) =>
    await catalog.FindAsync(code, ct) is { } product
        ? Results.Ok(product)
        : Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Producto sin precio",
            detail: $"El código {code} no tiene precio en ninguna cadena de La Plata."));

api.MapPost("/compare", async (CompareRequest request, CatalogService catalog, CancellationToken ct) =>
{
    if (request.Items is null || request.Items.Count == 0)
        return Results.Problem(statusCode: 400, title: "Lista vacía", detail: "Agregá al menos un producto.");
    if (request.Items.Count > CatalogService.MaxCartItems)
        return Results.Problem(statusCode: 400, title: "Lista demasiado larga",
            detail: $"Se comparan hasta {CatalogService.MaxCartItems} productos.");
    if (request.Items.Any(i => i.Quantity is < 1 or > CatalogService.MaxQuantity))
        return Results.Problem(statusCode: 400, title: "Cantidad inválida",
            detail: $"Cada cantidad tiene que estar entre 1 y {CatalogService.MaxQuantity}.");
    if (request.Threshold is < 0 or > 1)
        return Results.Problem(statusCode: 400, title: "Umbral inválido", detail: "El umbral va de 0 a 1 (0,03 = 3 %).");

    return Results.Ok(await catalog.CompareAsync(request, ct));
});

api.MapFallback(() => Results.Problem(statusCode: 404, title: "Ruta de API inexistente"));
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
