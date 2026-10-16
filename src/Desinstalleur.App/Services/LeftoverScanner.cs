using Desinstalleur.App.Models;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>
/// Cherche les dossiers, raccourcis et clés de registre laissés par un logiciel après sa désinstallation.
/// Ne supprime rien : il ne fait que proposer une liste que l'utilisateur valide.
/// </summary>
public sealed class LeftoverScanner
{
    private readonly SafetyGuard _guard;
    private readonly IReadOnlyList<InstalledProgram> _stillInstalled;

    /// <param name="stillInstalled">Logiciels encore présents : leurs dossiers et leurs noms sont exclus.</param>
    public LeftoverScanner(SafetyGuard guard, IReadOnlyList<InstalledProgram> stillInstalled)
    {
        _guard = guard;
        _stillInstalled = stillInstalled;
    }

    public List<LeftoverItem> Scan(InstalledProgram program)
    {
        var found = new Dictionary<string, LeftoverItem>(StringComparer.OrdinalIgnoreCase);

        // 1. Le dossier d'installation déclaré, s'il existe encore.
        if (!string.IsNullOrWhiteSpace(program.InstallLocation) && Directory.Exists(program.InstallLocation))
            AddFolder(found, program, program.InstallLocation!, MatchConfidence.High, L.T("Dossier d'installation déclaré par le logiciel"));

        // 2. Sous-dossiers portant le nom du logiciel (ou rangés dans un dossier à son éditeur).
        foreach (var root in KnownLocations.FolderRoots)
            ScanFolderRoot(found, program, root);

        // 2 bis. Dossiers cachés « .nom » à la racine du profil (ex. .ollama, .vscode).
        ScanDotFolders(found, program);

        // 3. Raccourcis restés sur le Bureau ou dans le menu Démarrer.
        foreach (var root in KnownLocations.ShortcutRoots)
            ScanShortcuts(found, program, root);

        // 4. Registre.
        ScanRegistry(found, program, RegistryHive.CurrentUser, @"SOFTWARE");
        ScanRegistry(found, program, RegistryHive.LocalMachine, @"SOFTWARE");
        ScanRegistry(found, program, RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node");
        AddOrphanUninstallKey(found, program);

        return RemoveNested(found.Values)
            .OrderByDescending(i => i.Confidence)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ScanFolderRoot(Dictionary<string, LeftoverItem> found, InstalledProgram program, string root)
    {
        foreach (var dir in SafeDirectories(root))
        {
            var name = Path.GetFileName(dir);
            var confidence = NameMatcher.Match(name, program.DisplayName, program.Publisher);
            if (confidence != MatchConfidence.None)
            {
                AddFolder(found, program, dir, confidence, L.F("Dossier nommé comme le logiciel dans {0}", Describe(root)));
                continue;
            }

            if (!NameMatcher.IsPublisherFolder(name, program.Publisher))
                continue;

            foreach (var sub in SafeDirectories(dir))
            {
                var subConfidence = NameMatcher.Match(Path.GetFileName(sub), program.DisplayName, program.Publisher);
                if (subConfidence != MatchConfidence.None)
                    AddFolder(found, program, sub, subConfidence, L.F("Dossier du logiciel chez son éditeur, dans {0}", Describe(root)));
            }
        }
    }

    private void ScanDotFolders(Dictionary<string, LeftoverItem> found, InstalledProgram program)
    {
        foreach (var dir in SafeDirectories(KnownLocations.UserProfile))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith('.'))
                continue;
            var confidence = NameMatcher.Match(name[1..], program.DisplayName, program.Publisher);
            if (confidence != MatchConfidence.None)
                AddFolder(found, program, dir, confidence, L.T("Dossier de données dans votre profil utilisateur"));
        }
    }

    private void ScanShortcuts(Dictionary<string, LeftoverItem> found, InstalledProgram program, string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.lnk").ToList();
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            var confidence = NameMatcher.Match(Path.GetFileNameWithoutExtension(file), program.DisplayName, program.Publisher);
            if (confidence == MatchConfidence.None || IsClaimedByInstalledProgram(file, Path.GetFileNameWithoutExtension(file)))
                continue;
            if (_guard.CheckPath(file) is not null)
                continue;

            found.TryAdd(file, new LeftoverItem
            {
                ProgramName = program.DisplayName,
                Kind = LeftoverKind.File,
                Path = file,
                SizeBytes = new FileInfo(file).Length,
                Confidence = confidence,
                Reason = L.T("Raccourci vers le logiciel supprimé"),
                IsSelected = confidence == MatchConfidence.High,
            });
        }
    }

    private void AddFolder(Dictionary<string, LeftoverItem> found, InstalledProgram program, string path, MatchConfidence confidence, string reason)
    {
        if (found.ContainsKey(path) || _guard.CheckPath(path) is not null)
            return;
        if (IsClaimedByInstalledProgram(path, Path.GetFileName(path)))
            return;

        try
        {
            if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
                return; // lien symbolique ou jonction : on ne le suit jamais
        }
        catch
        {
            return;
        }

        found[path] = new LeftoverItem
        {
            ProgramName = program.DisplayName,
            Kind = LeftoverKind.Folder,
            Path = path,
            SizeBytes = ProgramInventory.MeasureFolder(path),
            Confidence = confidence,
            Reason = reason,
            IsSelected = confidence == MatchConfidence.High,
        };
    }

    /// <summary>
    /// Vrai si un logiciel encore installé utilise ce dossier ou porte ce nom :
    /// on ne propose jamais de supprimer quelque chose qui sert encore.
    /// </summary>
    private bool IsClaimedByInstalledProgram(string path, string name)
    {
        var normalized = SafetyGuard.NormalizePath(path);
        foreach (var other in _stillInstalled)
        {
            var location = SafetyGuard.NormalizePath(other.InstallLocation);
            if (location.Length > 3 &&
                (normalized == location
                 || normalized.StartsWith(location + "\\", StringComparison.Ordinal)
                 || location.StartsWith(normalized + "\\", StringComparison.Ordinal)))
            {
                return true;
            }

            if (NameMatcher.Match(name, other.DisplayName, other.Publisher) == MatchConfidence.High)
                return true;
        }
        return false;
    }

    private void ScanRegistry(Dictionary<string, LeftoverItem> found, InstalledProgram program, RegistryHive hive, string softwarePath)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var software = root.OpenSubKey(softwarePath);
            if (software is null)
                return;

            foreach (var name in software.GetSubKeyNames())
            {
                var confidence = NameMatcher.Match(name, program.DisplayName, program.Publisher);
                if (confidence != MatchConfidence.None)
                {
                    AddKey(found, program, hive, softwarePath + "\\" + name, name, confidence, L.T("Clé de registre nommée comme le logiciel"));
                    continue;
                }

                if (!NameMatcher.IsPublisherFolder(name, program.Publisher))
                    continue;

                using var vendor = software.OpenSubKey(name);
                if (vendor is null)
                    continue;
                foreach (var sub in vendor.GetSubKeyNames())
                {
                    var subConfidence = NameMatcher.Match(sub, program.DisplayName, program.Publisher);
                    if (subConfidence != MatchConfidence.None)
                        AddKey(found, program, hive, softwarePath + "\\" + name + "\\" + sub, sub, subConfidence, L.T("Clé du logiciel chez son éditeur"));
                }
            }
        }
        catch
        {
            // Accès refusé à une partie du registre : on continue avec le reste.
        }
    }

    private void AddOrphanUninstallKey(Dictionary<string, LeftoverItem> found, InstalledProgram program)
    {
        if (ProgramInventory.StillExists(program))
            AddKey(found, program, program.Hive, program.KeyPath, null, MatchConfidence.High, L.T("Entrée de désinstallation orpheline"));
    }

    private void AddKey(Dictionary<string, LeftoverItem> found, InstalledProgram program, RegistryHive hive, string subKey,
        string? name, MatchConfidence confidence, string reason)
    {
        var full = (hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + subKey;
        if (found.ContainsKey(full) || SafetyGuard.CheckRegistryKey(full) is not null)
            return;
        if (name is not null && _stillInstalled.Any(o => NameMatcher.Match(name, o.DisplayName, o.Publisher) == MatchConfidence.High))
            return;

        found[full] = new LeftoverItem
        {
            ProgramName = program.DisplayName,
            Kind = LeftoverKind.RegistryKey,
            Path = full,
            Hive = hive,
            RegistrySubKey = subKey,
            Confidence = confidence,
            Reason = reason,
            IsSelected = confidence == MatchConfidence.High,
        };
    }

    /// <summary>Si un dossier est proposé, inutile de proposer aussi ce qu'il contient.</summary>
    private static IEnumerable<LeftoverItem> RemoveNested(IEnumerable<LeftoverItem> items)
    {
        var list = items.ToList();
        return list.Where(item => !list.Any(other =>
            !ReferenceEquals(other, item)
            && other.Kind != LeftoverKind.File
            && (other.Kind == LeftoverKind.RegistryKey) == (item.Kind == LeftoverKind.RegistryKey)
            && item.Path.StartsWith(other.Path + "\\", StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root) : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Describe(string root)
    {
        if (root.Equals(KnownLocations.ProgramFiles, StringComparison.OrdinalIgnoreCase)) return "Program Files";
        if (root.Equals(KnownLocations.ProgramFilesX86, StringComparison.OrdinalIgnoreCase)) return "Program Files (x86)";
        if (root.Equals(KnownLocations.ProgramData, StringComparison.OrdinalIgnoreCase)) return "ProgramData";
        if (root.Equals(KnownLocations.RoamingAppData, StringComparison.OrdinalIgnoreCase)) return "AppData\\Roaming";
        if (root.Equals(KnownLocations.LocalPrograms, StringComparison.OrdinalIgnoreCase)) return "AppData\\Local\\Programs";
        if (root.Equals(KnownLocations.LocalAppData, StringComparison.OrdinalIgnoreCase)) return "AppData\\Local";
        if (root.Equals(KnownLocations.LocalLow, StringComparison.OrdinalIgnoreCase)) return "AppData\\LocalLow";
        return L.T("le menu Démarrer");
    }
}
