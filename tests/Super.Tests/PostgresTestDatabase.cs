using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Super.Infrastructure.Persistence;

namespace Super.Tests;

/// <summary>
/// Base de PostgreSQL temporal para tests de integración: se crea con las migraciones y se borra al terminar.
/// Toma el servidor de la variable SUPER_TEST_DB o de appsettings.Local.json (ConnectionStrings:Super).
/// Si no hay servidor disponible, los tests que la usan se omiten.
/// </summary>
public abstract class PostgresTestBase : IAsyncLifetime
{
    private string? _adminConnection;
    private string? _databaseName;

    protected string? ConnectionString { get; private set; }
    protected string? SkipReason { get; private set; }

    protected SuperDbContext CreateContext()
    {
        Skip.If(ConnectionString is null, SkipReason);
        return new SuperDbContext(SuperDbContext.CreateOptions(ConnectionString!));
    }

    public async Task InitializeAsync()
    {
        var server = ResolveServer();
        if (server is null)
        {
            SkipReason = "Sin PostgreSQL: definí SUPER_TEST_DB o appsettings.Local.json.";
            return;
        }

        var admin = new NpgsqlConnectionStringBuilder(server) { Database = "postgres", Pooling = false };
        _adminConnection = admin.ConnectionString;
        _databaseName = $"super_test_{Guid.NewGuid():N}";

        try
        {
            await using var conn = new NpgsqlConnection(_adminConnection);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex)
        {
            SkipReason = $"PostgreSQL no disponible: {ex.Message}";
            _databaseName = null;
            return;
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = _databaseName }.ConnectionString;
        await using var db = new SuperDbContext(SuperDbContext.CreateOptions(ConnectionString));
        await db.Database.MigrateAsync();
    }

    public virtual async Task DisposeAsync()
    {
        if (_databaseName is null || _adminConnection is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_adminConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string? ResolveServer()
    {
        var env = Environment.GetEnvironmentVariable("SUPER_TEST_DB");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "appsettings.Local.json");
            if (!File.Exists(file)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
                && cs.TryGetProperty("Super", out var value)
                    ? value.GetString()
                    : null;
        }
        return null;
    }
}
