using Super.Domain;
using Super.Infrastructure.Sepa;

// Uso: dotnet run --project tools/Super.SepaProbe -- <id_comercio> <sucursales.csv> <productos.csv>
// Ejemplo: ... -- 12 raw/sepa_1_comercio-sepa-12_.../sucursales.csv raw/.../productos.csv
if (args.Length != 3)
{
    Console.WriteLine("Uso: Super.SepaProbe <id_comercio: 10|11|12> <sucursales.csv> <productos.csv>");
    return 1;
}

if (!ChainExtensions.TryFromSepaCommerceId(args[0], out var chain))
{
    Console.WriteLine("id_comercio no reconocido (10 = Carrefour, 11 = Changomás, 12 = COTO).");
    return 1;
}

using var sucursales = new StreamReader(args[1]);
using var productos = new StreamReader(args[2]);

var result = new SepaImporter().Import(chain, sucursales, productos);
var s = result.Stats;

Console.WriteLine($"{chain.DisplayName()}:");
Console.WriteLine($"  filas leídas ............ {s.RowsRead:N0}");
Console.WriteLine($"  filas en La Plata ....... {s.RowsInZone:N0}");
Console.WriteLine($"  sin EAN (peso variable) . {s.SkippedNoEan:N0}");
Console.WriteLine($"  EAN inválido ............ {s.SkippedInvalidEan:N0}");
Console.WriteLine($"  precio fuera de rango ... {s.SkippedBadPrice:N0}");
Console.WriteLine($"  aceptadas ............... {s.Accepted:N0}");
Console.WriteLine($"  EAN distintos ........... {SepaImporter.CheapestPerChain(result.Prices).Count():N0}");
return 0;
