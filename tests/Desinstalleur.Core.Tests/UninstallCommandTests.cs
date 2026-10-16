using Desinstalleur.Core;

namespace Desinstalleur.Core.Tests;

public class UninstallCommandTests
{
    [Fact]
    public void QuotedPathWithArguments()
    {
        var cmd = UninstallCommand.Parse("\"C:\\Program Files\\7-Zip\\Uninstall.exe\" /S")!;
        Assert.Equal(@"C:\Program Files\7-Zip\Uninstall.exe", cmd.FileName);
        Assert.Equal("/S", cmd.Arguments);
    }

    [Fact]
    public void UnquotedPathWithSpaces()
    {
        var cmd = UninstallCommand.Parse(@"C:\Program Files\Mozilla Firefox\uninstall\helper.exe /S")!;
        Assert.Equal(@"C:\Program Files\Mozilla Firefox\uninstall\helper.exe", cmd.FileName);
        Assert.Equal("/S", cmd.Arguments);
    }

    [Fact]
    public void MsiInstallSwitchIsTurnedIntoUninstall()
    {
        var cmd = UninstallCommand.Parse("MsiExec.exe /I{23170F69-40C1-2702-2201-000001000000}")!;
        Assert.True(cmd.IsMsi);
        Assert.Equal("/X{23170F69-40C1-2702-2201-000001000000}", cmd.Arguments);
    }

    [Fact]
    public void ExeInsideFolderNameIsNotCutTooEarly()
    {
        var cmd = UninstallCommand.Parse(@"C:\Tools\my.exe.files\unins000.exe")!;
        Assert.Equal(@"C:\Tools\my.exe.files\unins000.exe", cmd.FileName);
        Assert.Equal(string.Empty, cmd.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyValuesGiveNoCommand(string? value) => Assert.Null(UninstallCommand.Parse(value));
}
