using Super.Domain;

namespace Super.Tests;

public class EanTests
{
    [Fact]
    public void Normalize_PadsEan8To13Digits() =>
        Assert.Equal("0000040084107", Ean.Normalize("40084107"));

    [Fact]
    public void Normalize_SepaPaddedCodeEqualsScannerCode() =>
        Assert.Equal(Ean.Normalize("0000040084107"), Ean.Normalize("40084107"));

    [Fact]
    public void Normalize_TrimsSpaces() =>
        Assert.Equal("7790360981042", Ean.Normalize("  7790360981042 "));

    [Fact]
    public void Normalize_Gtin14WithLeadingZeroBecomesEan13() =>
        Assert.Equal("7790360981042", Ean.Normalize("07790360981042"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1234567")]            // 7 dígitos: demasiado corto
    [InlineData("123456789012345")]    // 15 dígitos: demasiado largo
    [InlineData("7790-360981042")]
    public void Normalize_RejectsInvalidCodes(string? raw) =>
        Assert.Null(Ean.Normalize(raw));

    [Fact]
    public void IsVariableWeight_DetectsInStoreCodes()
    {
        Assert.True(Ean.IsVariableWeight("2508398000001"));
        Assert.False(Ean.IsVariableWeight("7790360981042"));
        Assert.False(Ean.IsVariableWeight("0000040084107"));
    }

    [Theory]
    [InlineData("7790360981042", true)]
    [InlineData("7790360981043", false)]
    [InlineData("0000040084107", true)]  // EAN-8 40084107 rellenado
    public void HasValidCheckDigit_FollowsGs1Mod10(string code, bool expected) =>
        Assert.Equal(expected, Ean.HasValidCheckDigit(code));
}
