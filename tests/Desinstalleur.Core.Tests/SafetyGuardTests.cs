using Desinstalleur.Core;

namespace Desinstalleur.Core.Tests;

public class SafetyGuardTests
{
    private static readonly SafetyGuard Guard = new(
        protectedExact: new[] { @"C:\Users\Alex", @"C:\Users", @"C:\Users\Alex\AppData\Local\Programs" },
        protectedTrees: new[] { @"C:\Windows", @"C:\Program Files\WindowsApps", @"C:\Program Files\Common Files" },
        scanRoots: new[] { @"C:\Program Files", @"C:\ProgramData", @"C:\Users\Alex\AppData\Roaming", @"C:\Users\Alex\AppData\Local" });

    [Theory]
    [InlineData(@"C:\Program Files\VideoLAN\VLC")]
    [InlineData(@"C:\Users\Alex\AppData\Roaming\vlc")]
    [InlineData(@"D:\Jeux\MonJeu")]
    public void LeftoverFoldersAreAllowed(string path) => Assert.Null(Guard.CheckPath(path));

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\drivers")]
    [InlineData(@"c:/windows/system32")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files\")]
    [InlineData(@"C:\Program Files\Common Files\Something")]
    [InlineData(@"C:\Program Files\WindowsApps\App")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\Alex")]
    [InlineData(@"C:\Users\Alex\AppData\Roaming")]
    [InlineData(@"C:\Users\Alex\AppData\Local\Programs")]
    [InlineData(@"D:\Jeux")]
    [InlineData(@"C:\Program Files\..\Windows")]
    [InlineData(@"\\serveur\partage\dossier")]
    [InlineData(@"relative\path")]
    [InlineData("")]
    public void DangerousPathsAreRefused(string path) => Assert.NotNull(Guard.CheckPath(path));

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software\VideoLAN")]
    [InlineData(@"HKCU\Software\VideoLAN\VLC")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\VideoLAN")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VLC media player")]
    public void LeftoverKeysAreAllowed(string key) => Assert.Null(SafetyGuard.CheckRegistryKey(key));

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Office")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\A\B")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet")]
    [InlineData(@"HKEY_CLASSES_ROOT\.txt")]
    public void SystemKeysAreRefused(string key) => Assert.NotNull(SafetyGuard.CheckRegistryKey(key));
}
