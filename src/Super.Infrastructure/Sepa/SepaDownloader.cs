namespace Super.Infrastructure.Sepa;

/// <summary>
/// Descarga el dump diario de SEPA del portal de datos abiertos (datos.produccion.gob.ar, dataset sepa-precios).
/// Hay un archivo por día de la semana (sepa_lunes.zip … sepa_domingo.zip), cada uno con su id de recurso.
/// El portal está detrás de un firewall que rechaza pedidos sin encabezados de navegador, por eso se mandan.
/// </summary>
public sealed class SepaDownloader(HttpClient http)
{
    private const string DatasetUrl =
        "https://datos.produccion.gob.ar/dataset/6f47ec76-d1ce-4e34-a7e1-621fe9b1d0b5/resource";

    private static readonly IReadOnlyDictionary<DayOfWeek, (string Resource, string File)> Resources =
        new Dictionary<DayOfWeek, (string, string)>
        {
            [DayOfWeek.Sunday] = ("f8e75128-515a-436e-bf8d-5c63a62f2005", "sepa_domingo.zip"),
            [DayOfWeek.Monday] = ("0a9069a9-06e8-4f98-874d-da5578693290", "sepa_lunes.zip"),
            [DayOfWeek.Tuesday] = ("9dc06241-cc83-44f4-8e25-c9b1636b8bc8", "sepa_martes.zip"),
            [DayOfWeek.Wednesday] = ("1e92cd42-4f94-4071-a165-62c4cb2ce23c", "sepa_miercoles.zip"),
            [DayOfWeek.Thursday] = ("d076720f-a7f0-4af8-b1d6-1b99d5a90c14", "sepa_jueves.zip"),
            [DayOfWeek.Friday] = ("91bc072a-4726-44a1-85ec-4a8467aad27e", "sepa_viernes.zip"),
            [DayOfWeek.Saturday] = ("b3c3da5d-213d-41e7-8d74-f23fda0a3c30", "sepa_sabado.zip"),
        };

    public static Uri UrlFor(DayOfWeek day)
    {
        var (resource, file) = Resources[day];
        return new Uri($"{DatasetUrl}/{resource}/download/{file}");
    }

    /// <summary>Día de la semana actual en Argentina (el dump se nombra por el día local).</summary>
    public static DayOfWeek TodayInArgentina(TimeProvider? clock = null)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        return now.ToOffset(TimeSpan.FromHours(-3)).DayOfWeek;   // Argentina no usa horario de verano
    }

    /// <summary>Descarga el zip del día pedido a <paramref name="destination"/>, con hasta 3 intentos.</summary>
    public async Task DownloadAsync(DayOfWeek day, string destination, Action<string>? log = null,
        CancellationToken ct = default)
    {
        var url = UrlFor(day);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddBrowserHeaders(request);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(destination);
                await source.CopyToAsync(target, ct);
                log?.Invoke($"Descargado {url} ({target.Length / 1024 / 1024:N0} MB)");
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is HttpRequestException or IOException
                                       or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
            {
                log?.Invoke($"Intento {attempt} falló ({ex.Message}); se reintenta en {attempt * 20} s.");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 20), ct);
            }
        }
    }

    private static void AddBrowserHeaders(HttpRequestMessage request)
    {
        var h = request.Headers;
        h.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36");
        h.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/zip,*/*;q=0.8");
        h.TryAddWithoutValidation("Accept-Language", "es-AR,es;q=0.9,en;q=0.8");
        h.TryAddWithoutValidation("Referer", "https://datos.produccion.gob.ar/dataset/sepa-precios");
        h.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        h.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        h.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        h.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
    }
}
