using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

/// <summary>Dossiers où chercher des restes, et dossiers que l'on ne touche jamais.</summary>
public static class KnownLocations
{
    private static string F(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

    private static string Combine(string root, params string[] parts) =>
        string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(new[] { root }.Concat(parts).ToArray());

    public static string ProgramFiles => F(Environment.SpecialFolder.ProgramFiles);
    public static string ProgramFilesX86 => F(Environment.SpecialFolder.ProgramFilesX86);
    public static string ProgramData => F(Environment.SpecialFolder.CommonApplicationData);
    public static string RoamingAppData => F(Environment.SpecialFolder.ApplicationData);
    public static string LocalAppData => F(Environment.SpecialFolder.LocalApplicationData);
    public static string UserProfile => F(Environment.SpecialFolder.UserProfile);
    public static string LocalLow => Combine(UserProfile, "AppData", "LocalLow");
    public static string LocalPrograms => Combine(LocalAppData, "Programs");
    public static string StartMenuPrograms => F(Environment.SpecialFolder.Programs);
    public static string CommonStartMenuPrograms => F(Environment.SpecialFolder.CommonPrograms);
    public static string Desktop => F(Environment.SpecialFolder.DesktopDirectory);
    public static string CommonDesktop => F(Environment.SpecialFolder.CommonDesktopDirectory);

    /// <summary>Dossiers dont on examine les sous-dossiers à la recherche de restes.</summary>
    public static IReadOnlyList<string> FolderRoots => new[]
    {
        ProgramFiles, ProgramFilesX86, ProgramData, RoamingAppData, LocalAppData, LocalLow, LocalPrograms,
        StartMenuPrograms, CommonStartMenuPrograms,
    }.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Dossiers où l'on cherche seulement des raccourcis (.lnk).</summary>
    public static IReadOnlyList<string> ShortcutRoots => new[] { Desktop, CommonDesktop, StartMenuPrograms, CommonStartMenuPrograms }
        .Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static SafetyGuard CreateGuard()
    {
        var windows = F(Environment.SpecialFolder.Windows);
        var systemDrive = Path.GetPathRoot(windows) ?? @"C:\";
        var usersRoot = Path.GetDirectoryName(UserProfile) ?? Combine(systemDrive, "Users");

        var protectedExact = new[]
        {
            usersRoot, UserProfile, Combine(UserProfile, "AppData"), Desktop, CommonDesktop,
            F(Environment.SpecialFolder.MyDocuments), F(Environment.SpecialFolder.MyMusic),
            F(Environment.SpecialFolder.MyPictures), F(Environment.SpecialFolder.MyVideos),
            Combine(UserProfile, "Downloads"), Combine(UserProfile, "OneDrive"),
            F(Environment.SpecialFolder.StartMenu), F(Environment.SpecialFolder.CommonStartMenu),
            F(Environment.SpecialFolder.Startup), F(Environment.SpecialFolder.CommonStartup),
            Combine(LocalAppData, "Temp"), Combine(usersRoot, "Public"), Combine(usersRoot, "Default"),
        };

        var protectedTrees = new[]
        {
            windows,
            Combine(ProgramFiles, "WindowsApps"), Combine(ProgramFiles, "Common Files"), Combine(ProgramFilesX86, "Common Files"),
            Combine(ProgramFiles, "Windows Defender"), Combine(ProgramFiles, "Windows Defender Advanced Threat Protection"),
            Combine(ProgramFiles, "Windows NT"), Combine(ProgramFilesX86, "Windows NT"),
            Combine(ProgramFiles, "Windows Mail"), Combine(ProgramFiles, "Windows Media Player"),
            Combine(ProgramFiles, "Windows Photo Viewer"), Combine(ProgramFiles, "Windows Sidebar"),
            Combine(ProgramFiles, "WindowsPowerShell"), Combine(ProgramFilesX86, "WindowsPowerShell"),
            Combine(ProgramFiles, "Internet Explorer"), Combine(ProgramFilesX86, "Internet Explorer"),
            Combine(ProgramFiles, "dotnet"), Combine(ProgramFilesX86, "dotnet"),
            Combine(ProgramFilesX86, "Microsoft\\EdgeWebView"),
            Combine(ProgramData, "Microsoft"), Combine(ProgramData, "Package Cache"), Combine(ProgramData, "Packages"),
            Combine(RoamingAppData, "Microsoft"), Combine(LocalAppData, "Microsoft"), Combine(LocalAppData, "Packages"),
            Combine(systemDrive, "$Recycle.Bin"), Combine(systemDrive, "System Volume Information"),
            Combine(systemDrive, "Recovery"), Combine(systemDrive, "Boot"), Combine(systemDrive, "Config.Msi"),
            Combine(systemDrive, "Windows.old"),
        };

        var scanRoots = FolderRoots.Concat(ShortcutRoots);
        return new SafetyGuard(protectedExact, protectedTrees, scanRoots);
    }
}
