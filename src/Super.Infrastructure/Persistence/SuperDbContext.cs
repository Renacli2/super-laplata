using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Super.Infrastructure.Persistence;

public sealed class SuperDbContext(DbContextOptions<SuperDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<BranchPrice> BranchPrices => Set<BranchPrice>();
    public DbSet<ChainPrice> ChainPrices => Set<ChainPrice>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    /// <summary>Configuración común (Npgsql + nombres en snake_case).</summary>
    public static DbContextOptions<SuperDbContext> CreateOptions(string connectionString) =>
        Configure(new DbContextOptionsBuilder<SuperDbContext>(), connectionString).Options;

    /// <summary>
    /// Npgsql con reintentos ante fallas transitorias: Neon duerme la base cuando no se usa y la primera
    /// conexión puede fallar mientras despierta. Las transacciones propias deben ir dentro de
    /// <c>Database.CreateExecutionStrategy()</c> (la ingesta ya lo hace).
    /// </summary>
    public static DbContextOptionsBuilder<SuperDbContext> Configure(DbContextOptionsBuilder<SuperDbContext> builder,
        string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 4, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Product>(e =>
        {
            e.HasKey(p => p.Ean);
            e.Property(p => p.Ean).HasMaxLength(14);
            e.Property(p => p.Description).HasMaxLength(300);
            e.Property(p => p.Brand).HasMaxLength(150);
            e.Property(p => p.PresentationQuantity).HasPrecision(12, 3);
            e.Property(p => p.PresentationUnit).HasMaxLength(20);
        });

        model.Entity<Branch>(e =>
        {
            e.HasIndex(b => new { b.Chain, b.SepaBannerId, b.SepaBranchId }).IsUnique();
            e.Property(b => b.SepaBannerId).HasMaxLength(10);
            e.Property(b => b.SepaBranchId).HasMaxLength(20);
            e.Property(b => b.Name).HasMaxLength(150);
            e.Property(b => b.Street).HasMaxLength(150);
            e.Property(b => b.Number).HasMaxLength(20);
            e.Property(b => b.Locality).HasMaxLength(150);
        });

        model.Entity<BranchPrice>(e =>
        {
            e.HasKey(p => new { p.PriceDate, p.BranchId, p.Ean });
            e.HasIndex(p => new { p.Ean, p.PriceDate });
            e.Property(p => p.Ean).HasMaxLength(14);
            e.Property(p => p.ListPrice).HasPrecision(14, 2);
            e.Property(p => p.ReferencePrice).HasPrecision(14, 2);
            e.Property(p => p.PromoPrice).HasPrecision(14, 2);
            e.HasOne(p => p.Branch).WithMany().HasForeignKey(p => p.BranchId);
            e.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.Ean);
        });

        model.Entity<ChainPrice>(e =>
        {
            e.HasKey(p => new { p.PriceDate, p.Chain, p.Ean });
            e.HasIndex(p => new { p.Ean, p.PriceDate });
            e.Property(p => p.Ean).HasMaxLength(14);
            e.Property(p => p.MinListPrice).HasPrecision(14, 2);
            e.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.Ean);
        });

        model.Entity<ImportRun>(e =>
        {
            e.HasIndex(r => new { r.PriceDate, r.Chain }).IsUnique();
            e.Property(r => r.Source).HasMaxLength(300);
        });
    }
}

/// <summary>
/// Fábrica usada por <c>dotnet ef</c>. Toma la conexión de la variable SUPER_DB; para crear migraciones
/// no hace falta que la base exista.
/// </summary>
public sealed class SuperDbContextDesignFactory : IDesignTimeDbContextFactory<SuperDbContext>
{
    public SuperDbContext CreateDbContext(string[] args) =>
        new(SuperDbContext.CreateOptions(
            Environment.GetEnvironmentVariable("SUPER_DB")
            ?? "Host=localhost;Database=super;Username=postgres"));
}
