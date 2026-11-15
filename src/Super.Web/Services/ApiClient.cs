using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Super.Contracts;

namespace Super.Web.Services;

/// <summary>Error de la API con un mensaje para mostrar.</summary>
public sealed class ApiException(string message) : Exception(message);

/// <summary>Cliente de Super.Api (mismo origen que la app).</summary>
public sealed class ApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<ProductDto>> SearchAsync(string text, CancellationToken ct = default)
    {
        var url = $"api/products?q={Uri.EscapeDataString(text)}&limit=20";
        return await SendAsync(() => http.GetAsync(url, ct), ct) is { } response
            ? await response.Content.ReadFromJsonAsync<List<ProductDto>>(ct) ?? []
            : [];
    }

    /// <summary>Producto por código de barras; null si no tiene precio en ninguna cadena.</summary>
    public async Task<ProductDto?> FindAsync(string code, CancellationToken ct = default)
    {
        var response = await SendAsync(() => http.GetAsync($"api/products/{Uri.EscapeDataString(code)}", ct), ct,
            allowNotFound: true);
        return response is null ? null : await response.Content.ReadFromJsonAsync<ProductDto>(ct);
    }

    public async Task<CompareResponse> CompareAsync(CompareRequest request, CancellationToken ct = default)
    {
        var response = await SendAsync(() => http.PostAsJsonAsync("api/compare", request, ct), ct);
        return (await response!.Content.ReadFromJsonAsync<CompareResponse>(ct))!;
    }

    public async Task<StatusDto?> StatusAsync(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<StatusDto>("api/status", ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<HttpResponseMessage?> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct,
        bool allowNotFound = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException)
        {
            throw new ApiException("No hay conexión con el servidor. Revisá tu conexión a internet y probá de nuevo.");
        }

        if (response.IsSuccessStatusCode) return response;
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;

        var detail = await ReadProblemAsync(response, ct);
        throw new ApiException(detail ?? $"El servidor respondió con un error ({(int)response.StatusCode}).");
    }

    private static async Task<string?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString()
                : doc.RootElement.TryGetProperty("title", out var t) ? t.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
