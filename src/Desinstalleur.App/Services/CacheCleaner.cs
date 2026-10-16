using System.Diagnostics;
using System.Runtime.InteropServices;
using Desinstalleur.App.Models;

namespace Desinstalleur.App.Services;

/// <summary>
/// Liste des caches connus et nettoyage de leur contenu.
/// Seuls des dossiers de cache précis sont vidés : jamais de réglages, de sauvegardes ni de jeux.
/// Les fichiers utilisés par un programme ouvert sont simplement ignorés.
/// </summary>
public static class CacheCleaner
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static List<CacheTarget> BuildCatalog()
    {
        var fivem = Path.Combine(Local, "FiveM", "FiveM.app");
        return new List<CacheTarget>
        {
            new()
            {
                Name = "Fichiers temporaires",
                Description = "Fichiers laissés par Windows et les installations. Les fichiers de moins de 24 h sont gardés (réglable).",
                Glyph = "",
                Folders = new[] { Path.GetTempPath(), Path.Combine(WindowsDir, "Temp") },
                SkipRecentFiles = true,
                IsSelected = true,
            },
            new()
            {
                Name = "FiveM",
                Description = "Cache des serveurs, journaux et rapports de plantage. Les fichiers du jeu ne sont pas touchés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(fivem, "data", "cache"),
                    Path.Combine(fivem, "data", "server-cache"),
                    Path.Combine(fivem, "data", "server-cache-priv"),
                    Path.Combine(fivem, "crashes"),
                    Path.Combine(fivem, "logs"),
                },
                KeepSubfolders = new[] { "game" },
                Processes = new[] { "FiveM", "FiveM_GTAProcess", "FiveM_b3258_GTAProcess", "FiveM_ChromeBrowser" },
                AppName = "FiveM",
                IsSelected = true,
            },
            new()
            {
                Name = "Discord",
                Description = "Images et données mises en cache. Vos messages et votre compte ne sont pas touchés.",
                Glyph = "",
                Folders = new[] { "Cache", "Code Cache", "GPUCache" }.Select(f => Path.Combine(Roaming, "discord", f)).ToArray(),
                Processes = new[] { "Discord" },
                AppName = "Discord",
                IsSelected = true,
            },
            new()
            {
                Name = "Google Chrome",
                Description = "Cache des pages web. Historique, mots de passe et onglets sont conservés.",
                Glyph = "",
                Folders = BrowserCaches(Path.Combine(Local, "Google", "Chrome", "User Data")),
                Processes = new[] { "chrome" },
                AppName = "Google Chrome",
            },
            new()
            {
                Name = "Microsoft Edge",
                Description = "Cache des pages web. Historique, mots de passe et onglets sont conservés.",
                Glyph = "",
                Folders = BrowserCaches(Path.Combine(Local, "Microsoft", "Edge", "User Data")),
                Processes = new[] { "msedge" },
                AppName = "Microsoft Edge",
            },
            new()
            {
                Name = "Steam",
                Description = "Cache du navigateur intégré de Steam. Vos jeux ne sont pas touchés.",
                Glyph = "",
                Folders = new[] { Path.Combine(Local, "Steam", "htmlcache") },
                Processes = new[] { "steam", "steamwebhelper" },
                AppName = "Steam",
            },
            new()
            {
                Name = "Roblox",
                Description = "Journaux de Roblox et de Roblox Studio.",
                Glyph = "",
                Folders = new[] { Path.Combine(Local, "Roblox", "logs") },
                Processes = new[] { "RobloxPlayerBeta", "RobloxStudioBeta" },
                AppName = "Roblox",
            },
            new()
            {
                Name = "Cache des shaders",
                Description = "Shaders NVIDIA, AMD et DirectX. Ils seront recréés : les jeux peuvent saccader un peu au premier lancement.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "NVIDIA", "DXCache"),
                    Path.Combine(Local, "NVIDIA", "GLCache"),
                    Path.Combine(Local, "AMD", "DxCache"),
                    Path.Combine(Local, "AMD", "DxcCache"),
                    Path.Combine(Local, "D3DSCache"),
                },
            },
            new()
            {
                Name = "Rapports d'erreurs",
                Description = "Rapports de plantage et vidages mémoire conservés par Windows.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "CrashDumps"),
                    Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
                },
                IsSelected = true,
            },
            new()
            {
                Name = "Corbeille",
                Description = "Vide définitivement la Corbeille de tous les lecteurs.",
                Glyph = "",
                IsRecycleBin = true,
            },
        };
    }

    /// <summary>Dossiers de cache de chaque profil d'un navigateur basé sur Chromium.</summary>
    private static string[] BrowserCaches(string userData)
    {
        if (!Directory.Exists(userData))
            return Array.Empty<string>();

        var profiles = Directory.GetDirectories(userData)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
            });
        return profiles
            .SelectMany(p => new[] { Path.Combine(p, "Cache"), Path.Combine(p, "Code Cache"), Path.Combine(p, "GPUCache") })
            .ToArray();
    }

    public static long Measure(CacheTarget target)
    {
        if (target.IsRecycleBin)
            return RecycleBinSize();

        long total = 0;
        foreach (var folder in ExistingFolders(target))
        {
            foreach (var file in EnumerateFiles(folder))
            {
                if (ShouldSkip(target, folder, file))
                    continue;
                total += SafeLength(file);
            }
        }
        return total;
    }

    /// <summary>Noms des logiciels concernés qui sont ouverts en ce moment.</summary>
    public static IEnumerable<string> RunningApps(IEnumerable<CacheTarget> targets) =>
        targets.Where(t => t.AppName is not null && t.Processes.Any(IsRunning))
               .Select(t => t.AppName!)
               .Distinct();

    private static bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        var running = processes.Length > 0;
        foreach (var p in processes)
            p.Dispose();
        return running;
    }

    /// <summary>Vide le cache et renvoie le nombre d'octets libérés et de fichiers ignorés.</summary>
    public static (long Freed, int Skipped) Clean(CacheTarget target)
    {
        if (target.IsRecycleBin)
        {
            var size = RecycleBinSize();
            var result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            Journal.Write($"Corbeille vidée (code {result}).");
            return (result == 0 || result == unchecked((int)0x8000FFFF) ? size : 0, 0);
        }

        long freed = 0;
        var skipped = 0;
        foreach (var folder in ExistingFolders(target))
        {
            if (!IsSafeCacheFolder(folder))
            {
                Journal.Write($"Dossier de cache refusé par sécurité : {folder}");
                continue;
            }

            foreach (var file in EnumerateFiles(folder))
            {
                if (ShouldSkip(target, folder, file))
                    continue;
                var length = SafeLength(file);
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    freed += length;
                }
                catch
                {
                    skipped++; // fichier utilisé ou protégé
                }
            }

            RemoveEmptyDirectories(folder);
        }

        Journal.Write($"Cache « {target.Name} » nettoyé : {freed} octets libérés, {skipped} fichier(s) ignoré(s).");
        return (freed, skipped);
    }

    /// <summary>
    /// Aperçu de ce que le nettoyage supprimerait : un élément par fichier ou sous-dossier
    /// directement dans chaque dossier de cache, avec sa taille et son nombre de fichiers.
    /// </summary>
    public static CachePreview Preview(CacheTarget target)
    {
        var preview = new CachePreview(target);
        if (target.IsRecycleBin)
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems > 0)
                preview.Entries.Add(new PreviewEntry(L.T("Contenu de la Corbeille"), L.T("Corbeille de tous les lecteurs"), info.i64Size, (int)info.i64NumItems, false));
            return preview;
        }

        foreach (var folder in ExistingFolders(target))
        {
            var groups = new Dictionary<string, PreviewEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateFiles(folder))
            {
                if (ShouldSkip(target, folder, file))
                {
                    preview.KeptFiles++;
                    continue;
                }
                var first = Path.GetRelativePath(folder, file).Split('\\', '/')[0];
                var path = Path.Combine(folder, first);
                if (!groups.TryGetValue(path, out var entry))
                {
                    var isFolder = !string.Equals(path, file, StringComparison.OrdinalIgnoreCase);
                    entry = new PreviewEntry(first, path, 0, 0, isFolder);
                    groups[path] = entry;
                }
                entry.SizeBytes += SafeLength(file);
                entry.FileCount++;
            }
            preview.Entries.AddRange(groups.Values);
        }
        preview.Entries.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return preview;
    }

    /// <summary>Demande la fermeture des logiciels concernés (comme un clic sur la croix). Renvoie ceux encore ouverts.</summary>
    public static List<string> CloseApps(IEnumerable<CacheTarget> targets)
    {
        var list = targets.Where(t => t.AppName is not null).ToList();
        foreach (var target in list)
        {
            foreach (var name in target.Processes)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.CloseMainWindow();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && RunningApps(list).Any())
            Thread.Sleep(400);
        return RunningApps(list).ToList();
    }

    private static IEnumerable<string> ExistingFolders(CacheTarget target) =>
        target.Folders.Where(f => Directory.Exists(f) && !IsReparsePoint(f));

    private static IEnumerable<string> EnumerateFiles(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // ne jamais suivre un lien vers ailleurs
        };
        try
        {
            return Directory.EnumerateFiles(folder, "*", options).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool ShouldSkip(CacheTarget target, string folder, string file)
    {
        if (Favorites.IsProtectedPath(file))
            return true;

        if (target.KeepSubfolders.Count > 0)
        {
            var first = Path.GetRelativePath(folder, file).Split('\\', '/')[0];
            if (target.KeepSubfolders.Contains(first, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        if (!target.SkipRecentFiles || !AppSettings.Current.KeepRecentTempFiles)
            return false;
        try
        {
            return File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddHours(-24);
        }
        catch
        {
            return true;
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (IsReparsePoint(dir))
                    continue;
                RemoveEmptyDirectories(dir);
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch
                {
                    // dossier utilisé : on le laisse
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>Garde-fou : un dossier de cache n'est jamais une racine ou un dossier personnel.</summary>
    private static bool IsSafeCacheFolder(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd('\\');
        var forbidden = new[]
        {
            Path.GetPathRoot(full) ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Local, Roaming, ProgramData, WindowsDir,
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        return full.Length > 6 && !forbidden.Any(f => f.Length > 0 && full.Equals(f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch
        {
            return 0;
        }
    }

    // ----- Corbeille -----

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    private static long RecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }
}
