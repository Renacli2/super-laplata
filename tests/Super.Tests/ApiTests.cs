using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Super.Contracts;
using Super.Domain;
using Super.Infrastructure.Ingestion;
using Super.Infrastructure.Sepa;

namespace Super.Tests;

public sealed class ApiTests : PostgresTestBase
{
    private const string Aceite = "7790360981042";
    private const string Kinder = "0000040084107";
    private const string Fideos = "7790070318114";
    private static readonly DateOnly Day = new(2026, 11, 13);

    private WebApplicationFactory<Program>? _factory;

    public override async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task<HttpClient> ClientAsync()
    {
        await using (var db = CreateContext())
        {
            var service = new SepaIngestionService(db);
            await service.SaveAsync(Chain.Carrefour, Day, Prices(Chain.Carrefour, (Aceite, 3500m), (Kinder, 2800m), (Fideos, 1200m)), "test");
            await service.SaveAsync(Chain.Coto, Day, Prices(Chain.Coto, (Aceite, 3400m), (Kinder, 2769.99m)), "test");
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Super", ConnectionString));
        return _factory.CreateClient();
    }

    private static ImportResult Prices(Chain chain, params (string Ean, decimal Price)[] items)
    {
        var names = new Dictionary<string, string>
        {
            [Aceite] = "ACEITE GIRASOL X 900 ML",
            [Kinder] = "HUEVO KINDER 20 GR",
            [Fideos] = "FIDEOS 100% SEMOLA",
        };
        var prices = items
            .Select(i => new ImportedPrice(chain, "1", "1", i.Ean, names[i.Ean], "MARCA", i.Price, null, 900m, "ml", null))
            .ToList();
        var branch = new ImportedBranch(chain, "1", "1", "La Plata", "Calle 7", "1000", "La Plata", -34.92m, -57.95m);
        return new ImportResult(prices, new ImportStats(items.Length, items.Length, 0, 0, 0, items.Length), [branch]);
    }

    [SkippableFact]
    public async Task Status_ListsLatestImportPerChain()
    {
        var client = await ClientAsync();
        var status = await client.GetFromJsonAsync<StatusDto>("/api/status");

        Assert.Equal(["Carrefour", "Coto"], status!.Chains.Select(c => c.Chain));
        Assert.All(status.Chains, c => Assert.Equal(Day, c.PriceDate));
        Assert.Equal(3, status.Products);
    }

    [SkippableFact]
    public async Task Search_ByWords_ReturnsPricesCheapestFirst()
    {
        var client = await ClientAsync();
        var results = await client.GetFromJsonAsync<List<ProductDto>>("/api/products?q=aceite%20girasol");

        var aceite = Assert.Single(results!);
        Assert.Equal(Aceite, aceite.Ean);
        Assert.Equal("900 ml", aceite.Presentation);
        Assert.Equal(["COTO", "Carrefour"], aceite.Prices.Select(p => p.ChainName));
        Assert.Equal(3400m, aceite.Prices[0].Price);
    }

    [SkippableFact]
    public async Task Search_TreatsPercentAsText_NotAsWildcard()
    {
        var client = await ClientAsync();
        var results = await client.GetFromJsonAsync<List<ProductDto>>("/api/products?q=100%25");
        Assert.Equal(Fideos, Assert.Single(results!).Ean);

        Assert.Empty((await client.GetFromJsonAsync<List<ProductDto>>("/api/products?q=%25%25"))!);
    }

    [SkippableFact]
    public async Task Search_TooShortQuery_ReturnsEmpty()
    {
        var client = await ClientAsync();
        Assert.Empty((await client.GetFromJsonAsync<List<ProductDto>>("/api/products?q=a"))!);
    }

    [SkippableFact]
    public async Task FindByCode_AcceptsTheCodeAsTheCameraReadsIt()
    {
        var client = await ClientAsync();
        var kinder = await client.GetFromJsonAsync<ProductDto>("/api/products/40084107");

        Assert.Equal(Kinder, kinder!.Ean);
        Assert.Equal(2, kinder.Prices.Count);
    }

    [SkippableFact]
    public async Task FindByCode_UnknownOrInvalid_Returns404()
    {
        var client = await ClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/products/7790000000035")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/products/abc")).StatusCode);
    }

    [SkippableFact]
    public async Task Compare_RecommendsAndBuildsThePlan()
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/compare",
            new CompareRequest([new(Aceite, 2), new(Kinder, 1), new(Fideos, 1)]));
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<CompareResponse>())!;

        // COTO no tiene fideos: la única cadena completa es Carrefour (2×3500 + 2800 + 1200 = 11.000).
        // Repartido: 2×3400 + 2769,99 + 1200 = 10.769,99 → brecha 2,1 % ≤ 3 % → todo en Carrefour.
        Assert.Equal("Carrefour", result.BestSingleChain);
        Assert.Equal(11000m, result.BestSingleTotal);
        Assert.Equal(10769.99m, result.SplitTotal);
        Assert.True(result.RecommendSingleStore);
        var group = Assert.Single(result.Plan);
        Assert.Equal(11000m, group.Subtotal);
        Assert.Contains(group.Lines, l => l.Description == "HUEVO KINDER 20 GR");
        Assert.Null(result.Chains.Single(c => c.Chain == "Coto").Total);
    }

    [SkippableFact]
    public async Task Compare_WithLowerThreshold_SplitsTheList()
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync("/api/compare",
            new CompareRequest([new(Aceite, 2), new(Kinder, 1), new(Fideos, 1)], Threshold: 0.01m));
        var result = (await response.Content.ReadFromJsonAsync<CompareResponse>())!;

        Assert.False(result.RecommendSingleStore);
        Assert.Equal(["Carrefour", "Coto"], result.Plan.Select(p => p.Chain));
    }

    [SkippableTheory]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[{\"ean\":\"7790360981042\",\"quantity\":0}]}")]
    [InlineData("{\"items\":[{\"ean\":\"7790360981042\",\"quantity\":1}],\"threshold\":2}")]
    public async Task Compare_RejectsInvalidRequests(string json)
    {
        var client = await ClientAsync();
        var response = await client.PostAsync("/api/compare",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task ServesTheInstallableApp()
    {
        var client = await ClientAsync();

        var html = await client.GetStringAsync("/");
        Assert.Contains("manifest.webmanifest", html);
        Assert.Contains("_framework/blazor.webassembly", html);

        // El script de Blazor (con huella en el nombre) tiene que descargarse de verdad.
        var script = System.Text.RegularExpressions.Regex.Match(html, "_framework/blazor\\.webassembly[^\"]*\\.js").Value;
        Assert.NotEmpty(script);
        Assert.DoesNotContain("{fingerprint}", script);
        var scriptResponse = await client.GetAsync("/" + script);
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);

        var deepLink = await client.GetStringAsync("/comparar");
        Assert.Contains("<div id=\"app\">", deepLink);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/manifest.webmanifest")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/no-existe")).StatusCode);
    }
}
