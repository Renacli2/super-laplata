using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Super.Contracts;
using Super.Domain;
using Super.Infrastructure.Sepa;

namespace Super.Infrastructure.Export;

public sealed record ExportReport(
    DateOnly PriceDate,
    IReadOnlyList<(Chain Chain, DateOnly Date, ImportStats Stats, int Eans)> Chains,
    int Products,
    long CatalogBytes,
    long PricesBytes);

/// <summary>
/// Genera los archivos estáticos de la app (version.json, catalog.json, prices.json) a partir del dump de SEPA,
/// sin base de datos. Los precios son el mínimo de lista por cadena entre las sucursales de La Plata.
/// </summary>
public sealed class StaticDataExporter(SepaImporter? importer = null, TimeProvider? clock = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        // Deja los acentos legibles; los archivos se sirven como UTF-8.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SepaImporter _importer = importer ?? new SepaImporter();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<ExportReport> ExportAsync(IReadOnlyList<SepaChainFile> files, string outputDirectory,
        CancellationToken ct = default)
    {
        if (files.Count == 0) throw new InvalidDataException("El dump no tiene archivos de Carrefour, Changomás ni COTO.");

        var chains = new List<(Chain Chain, DateOnly Date, ImportStats Stats, int Eans)>();
        var prices = new Dictionary<string, Dictionary<Chain, decimal>>(StringComparer.Ordinal);
        var products = new Dictionary<string, CatalogProductDto>(StringComparer.Ordinal);

        foreach (var file in files.OrderBy(f => f.Chain))
        {
            ct.ThrowIfCancellationRequested();
            var result = await file.ReadAsync((suc, prod) => Task.FromResult(_importer.Import(file.Chain, suc, prod)));

            foreach (var quote in SepaImporter.CheapestPerChain(result.Prices))
            {
                if (!prices.TryGetValue(quote.Ean, out var byChain))
                    prices[quote.Ean] = byChain = [];
                byChain[quote.Chain] = quote.UnitPrice;
            }

            // Datos del producto: los de la primera cadena (en orden) que lo tenga.
            foreach (var p in result.Prices)
            {
                if (products.ContainsKey(p.Ean)) continue;
                products[p.Ean] = new CatalogProductDto(p.Ean, p.Description.Trim(), p.Brand.Trim(),
                    Presentation(p.PresentationQuantity, p.PresentationUnit));
            }

            chains.Add((file.Chain, file.Date, result.Stats, result.Prices.Select(p => p.Ean).Distinct().Count()));
        }

        var date = chains.Max(c => c.Date);
        var chainInfo = chains
            .Select(c => new ChainInfoDto(c.Chain.ToString(), c.Chain.DisplayName(), c.Date))
            .ToList();
        var order = chains.Select(c => c.Chain).ToList();

        var catalog = new CatalogFileDto(date, products.Values
            .OrderBy(p => p.Ean, StringComparer.Ordinal)
            .ToList());
        var priceFile = new PricesFileDto(date, chainInfo, prices
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new PriceRowDto(kv.Key, order.Select(c => kv.Value.TryGetValue(c, out var v) ? v : (decimal?)null).ToList()))
            .ToList());

        Directory.CreateDirectory(outputDirectory);
        var catalogBytes = await WriteAsync(Path.Combine(outputDirectory, "catalog.json"), catalog, ct);
        var pricesBytes = await WriteAsync(Path.Combine(outputDirectory, "prices.json"), priceFile, ct);
        // version.json va último: la app lo usa para saber que hay datos nuevos y completos.
        await WriteAsync(Path.Combine(outputDirectory, "version.json"),
            new DataVersionDto(date, _clock.GetUtcNow(), catalog.Products.Count), ct);

        return new ExportReport(date, chains, catalog.Products.Count, catalogBytes, pricesBytes);
    }

    private static async Task<long> WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        await using (var stream = File.Create(path))
            await JsonSerializer.SerializeAsync(stream, value, Json, ct);
        return new FileInfo(path).Length;
    }

    /// <summary>"900 ml", "0,8 kg"; vacío si falta algún dato.</summary>
    public static string Presentation(decimal? quantity, string unit)
    {
        unit = unit.Trim().ToLowerInvariant();
        return quantity is { } q && q > 0 && unit.Length > 0
            ? $"{q.ToString("0.###", CultureInfo.GetCultureInfo("es-AR"))} {unit}"
            : "";
    }
}
