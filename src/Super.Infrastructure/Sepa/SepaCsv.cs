using System.Globalization;

namespace Super.Infrastructure.Sepa;

/// <summary>Fila de un CSV de SEPA (separador '|', primera fila = encabezados).</summary>
public sealed class SepaRow
{
    private readonly string[] _fields;
    private readonly Dictionary<string, int> _index;

    internal SepaRow(string[] fields, Dictionary<string, int> index)
    {
        _fields = fields;
        _index = index;
    }

    /// <summary>Valor de la columna sin espacios; cadena vacía si la columna no existe.</summary>
    public string this[string column] =>
        _index.TryGetValue(column, out var i) && i < _fields.Length ? _fields[i].Trim() : string.Empty;

    public decimal? Decimal(string column)
    {
        var text = this[column];
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}

public static class SepaCsv
{
    /// <summary>
    /// Lee un CSV de SEPA en streaming (los productos.csv pesan cientos de MB).
    /// Descarta el BOM, las líneas vacías y las filas con menos campos que encabezados.
    /// </summary>
    public static IEnumerable<SepaRow> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var header = reader.ReadLine();
        if (header is null) yield break;

        var headers = header.TrimStart('﻿').Split('|');
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
            index.TryAdd(headers[i].Trim(), i);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var fields = line.Split('|');
            if (fields.Length < headers.Length) continue;
            yield return new SepaRow(fields, index);
        }
    }
}
