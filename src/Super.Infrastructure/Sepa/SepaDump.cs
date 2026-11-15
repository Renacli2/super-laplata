using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Super.Domain;

namespace Super.Infrastructure.Sepa;

/// <summary>
/// Archivo de una cadena dentro del dump diario de SEPA
/// (p. ej. <c>sepa_1_comercio-sepa-12_2026-11-13_09-05-10.zip</c>, con sucursales.csv y productos.csv adentro).
/// </summary>
public sealed partial class SepaChainFile : IDisposable
{
    private readonly string _zipPath;
    private readonly bool _deleteOnDispose;

    internal SepaChainFile(Chain chain, DateOnly date, string name, string zipPath, bool deleteOnDispose)
    {
        Chain = chain;
        Date = date;
        Name = name;
        _zipPath = zipPath;
        _deleteOnDispose = deleteOnDispose;
    }

    public Chain Chain { get; }
    public DateOnly Date { get; }
    public string Name { get; }

    /// <summary>Lee sucursales.csv (en memoria, es chico) y abre productos.csv en streaming.</summary>
    public async Task<T> ReadAsync<T>(Func<TextReader, TextReader, Task<T>> action)
    {
        using var zip = ZipFile.OpenRead(_zipPath);
        var sucursalesEntry = FindEntry(zip, "sucursales.csv");
        var productosEntry = FindEntry(zip, "productos.csv");

        string sucursalesText;
        using (var reader = new StreamReader(sucursalesEntry.Open()))
            sucursalesText = await reader.ReadToEndAsync();

        using var sucursales = new StringReader(sucursalesText);
        using var productos = new StreamReader(productosEntry.Open());
        return await action(sucursales, productos);
    }

    private ZipArchiveEntry FindEntry(ZipArchive zip, string fileName) =>
        zip.Entries.FirstOrDefault(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"{Name} no contiene {fileName}.");

    public void Dispose()
    {
        if (_deleteOnDispose && File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    [GeneratedRegex(@"comercio-sepa-(?<id>\d+)_(?<date>\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    internal static partial Regex NamePattern();
}

public static class SepaDump
{
    /// <summary>
    /// Busca los archivos de las cadenas comparadas (Carrefour, Changomás, COTO) en <paramref name="path"/>, que puede ser:
    /// el zip semanal (<c>sepa_viernes.zip</c>, con los zips por comercio adentro), una carpeta con esos zips,
    /// o directamente el zip de un comercio. Los zips anidados se extraen a una carpeta temporal.
    /// </summary>
    public static IReadOnlyList<SepaChainFile> Open(string path, string? tempDirectory = null)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.zip", SearchOption.AllDirectories)
                .Select(f => TryCreate(Path.GetFileName(f), f, deleteOnDispose: false))
                .OfType<SepaChainFile>()
                .OrderBy(f => f.Chain)
                .ToList();
        }

        if (!File.Exists(path)) throw new FileNotFoundException("No existe el archivo o carpeta del dump.", path);

        var direct = TryCreate(Path.GetFileName(path), path, deleteOnDispose: false);
        if (direct is not null) return [direct];

        // Zip contenedor: extraer solo los zips de las cadenas que interesan.
        var temp = tempDirectory ?? Path.Combine(Path.GetTempPath(), "super-sepa");
        Directory.CreateDirectory(temp);
        var result = new List<SepaChainFile>();
        using var outer = ZipFile.OpenRead(path);
        foreach (var entry in outer.Entries)
        {
            if (!TryParseName(entry.Name, out _, out _)) continue;
            var target = Path.Combine(temp, $"{Guid.NewGuid():N}_{entry.Name}");
            entry.ExtractToFile(target);
            result.Add(TryCreate(entry.Name, target, deleteOnDispose: true)!);
        }
        return result.OrderBy(f => f.Chain).ToList();
    }

    /// <summary>Extrae cadena y fecha del nombre del zip del comercio; false si no es una cadena comparada.</summary>
    public static bool TryParseName(string fileName, out Chain chain, out DateOnly date)
    {
        chain = default;
        date = default;
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        var match = SepaChainFile.NamePattern().Match(fileName);
        return match.Success
            && ChainExtensions.TryFromSepaCommerceId(match.Groups["id"].Value, out chain)
            && DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
    }

    private static SepaChainFile? TryCreate(string name, string zipPath, bool deleteOnDispose) =>
        TryParseName(name, out var chain, out var date)
            ? new SepaChainFile(chain, date, name, zipPath, deleteOnDispose)
            : null;
}
