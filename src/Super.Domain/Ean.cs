namespace Super.Domain;

/// <summary>
/// Normalización de códigos de barras. SEPA guarda los EAN rellenados con ceros a 13 dígitos
/// (un EAN-8 como 40084107 llega como 0000040084107), mientras que la cámara lee el código original.
/// Todo se compara en su forma canónica de 13 dígitos (o 14 si es un GTIN-14 que no empieza con 0).
/// </summary>
public static class Ean
{
    /// <summary>Devuelve la forma canónica o null si el texto no es un código numérico de 8 a 14 dígitos.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim();
        foreach (var c in s)
        {
            if (c < '0' || c > '9') return null;
        }

        if (s.Length < 8 || s.Length > 14) return null;

        if (s.Length <= 13) return s.PadLeft(13, '0');

        // 14 dígitos: si empieza con 0 es un EAN-13 con indicador cero.
        return s[0] == '0' ? s[1..] : s;
    }

    /// <summary>
    /// Códigos de peso variable generados por el propio comercio (balanza): 13 dígitos que empiezan con 2.
    /// No son comparables entre cadenas: se tratan como ítems libres.
    /// </summary>
    public static bool IsVariableWeight(string? normalized) =>
        normalized is { Length: 13 } && normalized[0] == '2';

    /// <summary>Verifica el dígito de control GS1 (módulo 10) de un código ya normalizado.</summary>
    public static bool HasValidCheckDigit(string? normalized)
    {
        if (string.IsNullOrEmpty(normalized) || normalized.Length < 8) return false;

        var n = normalized.Length;
        var sum = 0;
        for (var i = n - 2; i >= 0; i--)
        {
            var digit = normalized[i] - '0';
            var weight = ((n - 2 - i) % 2 == 0) ? 3 : 1;
            sum += digit * weight;
        }

        var expected = (10 - (sum % 10)) % 10;
        return expected == normalized[n - 1] - '0';
    }
}
