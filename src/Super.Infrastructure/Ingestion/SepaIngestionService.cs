using Microsoft.EntityFrameworkCore;
using Super.Domain;
using Super.Infrastructure.Persistence;
using Super.Infrastructure.Sepa;

namespace Super.Infrastructure.Ingestion;

public sealed record IngestionReport(
    Chain Chain,
    DateOnly PriceDate,
    ImportStats Stats,
    int Branches,
    int DistinctProducts,
    int NewProducts,
    bool ReplacedPreviousImport);

/// <summary>
/// Guarda en la base los precios de un dump de SEPA para una cadena y un día.
/// Es idempotente: reimportar la misma (cadena, fecha) reemplaza los precios anteriores de ese día.
/// </summary>
public sealed class SepaIngestionService(SuperDbContext db, SepaImporter? importer = null, TimeProvider? clock = null)
{
    private readonly SepaImporter _importer = importer ?? new SepaImporter();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IngestionReport> IngestAsync(Chain chain, DateOnly date, TextReader sucursales, TextReader productos,
        string source, CancellationToken ct = default)
    {
        var result = _importer.Import(chain, sucursales, productos);
        return SaveAsync(chain, date, result, source, ct);
    }

    public async Task<IngestionReport> SaveAsync(Chain chain, DateOnly date, ImportResult result, string source,
        CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // 1) Borrar lo importado antes para esta cadena y fecha.
            var chainBranchIds = db.Branches.Where(b => b.Chain == chain).Select(b => b.Id);
            await db.BranchPrices
                .Where(p => p.PriceDate == date && chainBranchIds.Contains(p.BranchId))
                .ExecuteDeleteAsync(ct);
            await db.ChainPrices.Where(p => p.PriceDate == date && p.Chain == chain).ExecuteDeleteAsync(ct);
            var replaced = await db.ImportRuns.Where(r => r.PriceDate == date && r.Chain == chain).ExecuteDeleteAsync(ct) > 0;

            // 2) Sucursales: alta o actualización.
            var branches = await db.Branches
                .Where(b => b.Chain == chain)
                .ToDictionaryAsync(b => (b.SepaBannerId, b.SepaBranchId), ct);
            foreach (var ib in result.Branches)
            {
                if (!branches.TryGetValue((ib.BannerId, ib.BranchId), out var branch))
                {
                    branch = new Branch { Chain = chain, SepaBannerId = ib.BannerId, SepaBranchId = ib.BranchId };
                    db.Branches.Add(branch);
                    branches[(ib.BannerId, ib.BranchId)] = branch;
                }

                if (date >= branch.LastSeenOn)
                {
                    branch.Name = Clip(ib.Name, 150);
                    branch.Street = Clip(ib.Street, 150);
                    branch.Number = Clip(ib.Number, 20);
                    branch.Locality = Clip(ib.Locality, 150);
                    branch.Latitude = (double?)ib.Latitude;
                    branch.Longitude = (double?)ib.Longitude;
                    branch.LastSeenOn = date;
                }
            }
            await db.SaveChangesAsync(ct);

            // 3) Productos: alta de los nuevos; los existentes solo completan datos vacíos.
            var byEan = result.Prices.GroupBy(p => p.Ean).ToList();
            var eans = byEan.Select(g => g.Key).ToList();
            var products = await db.Products.Where(p => eans.Contains(p.Ean)).ToDictionaryAsync(p => p.Ean, ct);
            var newProducts = 0;
            foreach (var group in byEan)
            {
                var sample = group.First();
                if (!products.TryGetValue(group.Key, out var product))
                {
                    product = new Product { Ean = group.Key, FirstSeenOn = date, LastSeenOn = date };
                    db.Products.Add(product);
                    products[group.Key] = product;
                    newProducts++;
                }

                if (product.Description.Length == 0) product.Description = Clip(sample.Description, 300);
                if (product.Brand.Length == 0) product.Brand = Clip(sample.Brand, 150);
                if (product.PresentationUnit.Length == 0) product.PresentationUnit = Clip(sample.PresentationUnit, 20);
                product.PresentationQuantity ??= sample.PresentationQuantity;
                if (date > product.LastSeenOn) product.LastSeenOn = date;
                if (date < product.FirstSeenOn) product.FirstSeenOn = date;
            }

            // 4) Precios por sucursal (si una sucursal repite el EAN, queda el más barato).
            foreach (var g in result.Prices.GroupBy(p => (p.BannerId, p.BranchId, p.Ean)))
            {
                var cheapest = g.MinBy(p => p.ListPrice)!;
                db.BranchPrices.Add(new BranchPrice
                {
                    PriceDate = date,
                    BranchId = branches[(g.Key.BannerId, g.Key.BranchId)].Id,
                    Ean = g.Key.Ean,
                    ListPrice = cheapest.ListPrice,
                    ReferencePrice = cheapest.ReferencePrice,
                    PromoPrice = g.Select(p => p.PromoPrice).Where(p => p > 0m).Min(),
                });
            }

            // 5) Precio mínimo por cadena (lo que usa el comparador).
            foreach (var group in byEan)
            {
                db.ChainPrices.Add(new ChainPrice
                {
                    PriceDate = date,
                    Chain = chain,
                    Ean = group.Key,
                    MinListPrice = group.Min(p => p.ListPrice),
                    BranchCount = group.Select(p => (p.BannerId, p.BranchId)).Distinct().Count(),
                });
            }

            var s = result.Stats;
            db.ImportRuns.Add(new ImportRun
            {
                PriceDate = date,
                Chain = chain,
                Source = Clip(source, 300),
                ImportedAt = _clock.GetUtcNow(),
                RowsRead = s.RowsRead,
                RowsInZone = s.RowsInZone,
                SkippedNoEan = s.SkippedNoEan,
                SkippedInvalidEan = s.SkippedInvalidEan,
                SkippedBadPrice = s.SkippedBadPrice,
                Accepted = s.Accepted,
                Branches = result.Branches.Count,
                DistinctProducts = byEan.Count,
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();

            return new IngestionReport(chain, date, s, result.Branches.Count, byEan.Count, newProducts, replaced);
        });
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
