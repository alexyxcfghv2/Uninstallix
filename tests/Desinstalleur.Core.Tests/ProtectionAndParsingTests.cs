using Desinstalleur.Core;

namespace Desinstalleur.Core.Tests;

public class ProtectionAndParsingTests
{
    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38.33130")]
    [InlineData("Microsoft .NET Runtime - 8.0.10 (x64)")]
    [InlineData("Microsoft Windows Desktop Runtime - 8.0.10 (x64)")]
    [InlineData("Microsoft Edge WebView2 Runtime")]
    [InlineData("Realtek High Definition Audio Driver")]
    [InlineData("NVIDIA Graphics Driver 560.94")]
    public void EssentialComponentsAreProtected(string name) =>
        Assert.NotNull(ProtectionRules.GetProtectionReason(name, "x", isSystemComponent: false));

    [Theory]
    [InlineData("VLC media player")]
    [InlineData("Mozilla Firefox (x64 fr)")]
    [InlineData("Microsoft Teams")]
    [InlineData("Steam")]
    public void RegularAppsAreNotProtected(string name) =>
        Assert.Null(ProtectionRules.GetProtectionReason(name, "x", isSystemComponent: false));

    [Fact]
    public void SystemComponentFlagAlwaysProtects() =>
        Assert.NotNull(ProtectionRules.GetProtectionReason("Anything", null, isSystemComponent: true));

    [Theory]
    [InlineData("20240315", 2024, 3, 15)]
    [InlineData("2023-11-02", 2023, 11, 2)]
    public void InstallDatesAreParsed(string raw, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d), ValueParsers.ParseInstallDate(raw));

    [Theory]
    [InlineData("19700101")]
    [InlineData("n'importe quoi")]
    [InlineData(null)]
    public void BadDatesAreIgnored(string? raw) => Assert.Null(ValueParsers.ParseInstallDate(raw));

    [Fact]
    public void SizesAreFormattedInFrench()
    {
        Assert.Equal("—", ValueParsers.FormatSize(null));
        Assert.Equal("1,5 Mo", ValueParsers.FormatSize(1536 * 1024));
        Assert.Equal(2048L, ValueParsers.EstimatedSizeToBytes(2));
    }
}
