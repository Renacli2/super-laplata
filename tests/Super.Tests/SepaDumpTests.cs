using System.IO.Compression;
using System.Text;
using Super.Domain;
using Super.Infrastructure.Sepa;

namespace Super.Tests;

public sealed class SepaDumpTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("super-dump-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("sepa_1_comercio-sepa-12_2026-11-13_09-05-10.zip", true, Chain.Coto)]
    [InlineData("sepa_2_comercio-sepa-10_2026-11-13_01-05-07.zip", true, Chain.Carrefour)]
    [InlineData("sepa_2_comercio-sepa-11_2026-11-13_01-05-07.zip", true, Chain.Changomas)]
    [InlineData("sepa_2_comercio-sepa-15_2026-11-13_01-05-07.zip", false, default(Chain))]   // otra cadena
    [InlineData("sepa_1_comercio-sepa-12_2026-11-13_09-05-10.csv", false, default(Chain))]
    [InlineData("cualquier-cosa.zip", false, default(Chain))]
    public void TryParseName_RecognizesComparedChains(string name, bool ok, Chain chain)
    {
        Assert.Equal(ok, SepaDump.TryParseName(name, out var parsed, out var date));
        if (!ok) return;
        Assert.Equal(chain, parsed);
        Assert.Equal(new DateOnly(2026, 11, 13), date);
    }

    [Fact]
    public async Task Open_OuterZip_ExtractsOnlyComparedChains_AndReadsBothCsv()
    {
        var outer = Path.Combine(_dir, "sepa_viernes.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
        {
            AddInnerZip(zip, "2026-11-13/sepa_1_comercio-sepa-12_2026-11-13_09-05-10.zip", "COTO");
            AddInnerZip(zip, "2026-11-13/sepa_2_comercio-sepa-10_2026-11-13_01-05-07.zip", "CARREFOUR");
            AddInnerZip(zip, "2026-11-13/sepa_2_comercio-sepa-15_2026-11-13_01-05-07.zip", "OTRA");
        }

        var temp = Path.Combine(_dir, "tmp");
        var files = SepaDump.Open(outer, temp);
        try
        {
            Assert.Equal([Chain.Carrefour, Chain.Coto], files.Select(f => f.Chain));
            var text = await files[1].ReadAsync(async (suc, prod) => await suc.ReadToEndAsync() + "/" + await prod.ReadToEndAsync());
            Assert.Equal("sucursales COTO/productos COTO", text);
        }
        finally
        {
            foreach (var f in files) f.Dispose();
        }

        Assert.Empty(Directory.GetFiles(temp));   // los zips extraídos se borran al terminar
    }

    [Fact]
    public void Open_Folder_FindsInnerZips()
    {
        using (var zip = ZipFile.Open(Path.Combine(_dir, "sepa_1_comercio-sepa-12_2026-11-14_09-05-10.zip"), ZipArchiveMode.Create))
        {
            AddText(zip, "sucursales.csv", "x");
            AddText(zip, "productos.csv", "y");
        }

        var file = Assert.Single(SepaDump.Open(_dir));
        Assert.Equal(Chain.Coto, file.Chain);
        Assert.Equal(new DateOnly(2026, 11, 14), file.Date);
    }

    private static void AddInnerZip(ZipArchive outer, string name, string tag)
    {
        using var buffer = new MemoryStream();
        using (var inner = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(inner, "comercio.csv", "comercio " + tag);
            AddText(inner, "sucursales.csv", "sucursales " + tag);
            AddText(inner, "productos.csv", "productos " + tag);
        }
        using var entry = outer.CreateEntry(name).Open();
        buffer.Position = 0;
        buffer.CopyTo(entry);
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
