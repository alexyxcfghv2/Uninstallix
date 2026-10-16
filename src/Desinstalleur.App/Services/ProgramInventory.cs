using Desinstalleur.App.Models;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>Lit la liste des logiciels installés depuis les clés « Uninstall » du registre.</summary>
public static class ProgramInventory
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly HashSet<string> UpdateReleaseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Security Update", "Update Rollup", "Hotfix", "Update", "ServicePack",
    };

    public static List<InstalledProgram> Load()
    {
        var programs = new List<InstalledProgram>();
        ReadFrom(RegistryHive.LocalMachine, UninstallKey, programs);
        ReadFrom(RegistryHive.LocalMachine, UninstallKey32, programs);
        ReadFrom(RegistryHive.CurrentUser, UninstallKey, programs);

        // Un même logiciel peut apparaître dans plusieurs vues : on garde la première entrée.
        return programs
            .GroupBy(p => (p.DisplayName.ToLowerInvariant(), p.Version, p.Publisher))
            .Select(g => g.First())
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool StillExists(InstalledProgram program)
    {
        using var root = RegistryKey.OpenBaseKey(program.Hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(program.KeyPath);
        return key is not null;
    }

    private static void ReadFrom(RegistryHive hive, string path, List<InstalledProgram> output)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var uninstall = root.OpenSubKey(path);
        if (uninstall is null)
            return;

        foreach (var name in uninstall.GetSubKeyNames())
        {
            try
            {
                using var key = uninstall.OpenSubKey(name);
                if (key is null)
                    continue;
                var program = ReadProgram(key, hive, path + "\\" + name);
                if (program is not null)
                    output.Add(program);
            }
            catch
            {
                // Clé illisible (droits, valeur corrompue) : on l'ignore.
            }
        }
    }

    private static InstalledProgram? ReadProgram(RegistryKey key, RegistryHive hive, string keyPath)
    {
        var displayName = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        // Mises à jour et correctifs rattachés à un autre logiciel : ils ne s'affichent pas.
        if (key.GetValue("ParentKeyName") is string parent && parent.Length > 0)
            return null;
        if (key.GetValue("ReleaseType") is string release && UpdateReleaseTypes.Contains(release))
            return null;

        var uninstallString = key.GetValue("UninstallString") as string;
        if (string.IsNullOrWhiteSpace(uninstallString))
            return null;

        var isSystemComponent = key.GetValue("SystemComponent") is int sc && sc == 1;
        var publisher = (key.GetValue("Publisher") as string)?.Trim();
        var installDate = ValueParsers.ParseInstallDate(key.GetValue("InstallDate")?.ToString());
        var estimated = false;
        if (installDate is null)
        {
            installDate = NativeMethods.GetLastWriteTime(key)?.Date;
            estimated = installDate is not null;
        }

        var location = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');

        return new InstalledProgram
        {
            DisplayName = displayName,
            Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher,
            Version = (key.GetValue("DisplayVersion") as string)?.Trim(),
            InstallLocation = string.IsNullOrWhiteSpace(location) ? null : location,
            DisplayIconPath = key.GetValue("DisplayIcon") as string,
            UninstallString = uninstallString,
            QuietUninstallString = key.GetValue("QuietUninstallString") as string,
            IsSystemComponent = isSystemComponent,
            IsMsi = key.GetValue("WindowsInstaller") is int wi && wi == 1,
            Hive = hive,
            KeyPath = keyPath,
            InstallDate = installDate,
            InstallDateIsEstimated = estimated,
            SizeBytes = ValueParsers.EstimatedSizeToBytes(key.GetValue("EstimatedSize")),
            ProtectionReason = displayName == "Uninstallix"
                ? L.T("C'est Uninstallix lui-même : désinstallez-le depuis « Applications installées » de Windows.")
                : Favorites.IsProtectedProgram(displayName)
                ? L.T("il est dans vos favoris protégés (Réglages › Favoris protégés)")
                : TranslateReason(ProtectionRules.GetProtectionReason(displayName, publisher, isSystemComponent)),
        };
    }

    /// <summary>Traduit la raison de protection renvoyée par le moteur (texte français).</summary>
    private static string? TranslateReason(string? reason) => reason is null ? null : L.T(reason);

    /// <summary>
    /// Calcule la taille du dossier d'installation pour les logiciels qui ne la déclarent pas.
    /// S'arrête au bout d'un nombre limité de fichiers pour rester rapide.
    /// </summary>
    public static long? MeasureFolder(string? folder, int maxFiles = 200_000)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;

        long total = 0;
        var count = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                total += file.Length;
                if (++count >= maxFiles)
                    break;
            }
        }
        catch
        {
            return total > 0 ? total : null;
        }
        return total > 0 ? total : null;
    }
}
