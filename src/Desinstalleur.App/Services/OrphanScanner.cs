using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Desinstalleur.App.Models;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>
/// Cherche les « restes oubliés » : dossiers de premier niveau (Program Files, ProgramData, AppData)
/// dont aucun logiciel installé, service, programme de démarrage ou processus en cours ne se sert.
/// Volontairement prudent : en cas de doute, le dossier n'est pas proposé. Ne supprime rien.
/// </summary>
public sealed class OrphanScanner
{
    /// <summary>Un dossier modifié depuis moins longtemps sert sans doute encore : il n'est pas proposé.</summary>
    public const int MinAgeDays = 30;

    /// <summary>En dessous, le dossier est masqué par défaut (case « Afficher les petits dossiers »).</summary>
    public const long SmallFolderBytes = 1024 * 1024;

    private const int MaxFilesPerFolder = 500_000;

    private static readonly Regex GuidName = new(@"^\{?[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}\}?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Noms (forme compacte) jamais proposés : Windows, pilotes, gestionnaires de paquets, caches partagés.</summary>
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.Ordinal)
    {
        "microsoft", "windows", "windowsapps", "modifiablewindowsapps", "packages", "temp", "tmp",
        "nvidia", "nvidiacorporation", "intel", "amd", "ati", "realtek", "commonfiles", "internetexplorer",
        "msbuild", "referenceassemblies", "dotnet", "packagecache", "packagemanagement", "ssh",
        "usoshared", "usoprivate", "softwaredistribution", "comms", "connecteddevicesplatform", "d3dscache",
        "crashdumps", "virtualstore", "history", "inetcache", "uninstallix", "desinstalleur", "applicationdata",
        "placeholdertilelogofolder", "peerdistrepub", "elevateddiagnostics", "toastnotificationmanagercompat",
        "isolatedstorage", "publishers", "uninstallinformation", "mediacenterprograms", "macromedia",
        "identities", "boostinterprocess", "hyperv", "iis", "wsl", "oem", "driverstore",
        "nuget", "npm", "npmcache", "pip", "pypoetry", "yarn", "pnpm", "composer", "gradle",
        "chocolatey", "scoop", "winget", "squirreltemp", "crashpad", "electron", "cef", "fontconfig",
        "startmenu", "templates", "favorites", "documents", "desktop",
    };

    /// <summary>Premiers mots trop vagues pour désigner un logiciel (« Free », « Visual »…).</summary>
    private static readonly HashSet<string> VagueWords = new(StringComparer.Ordinal)
    {
        "free", "open", "pro", "online", "desktop", "studio", "driver", "service", "update", "runtime",
        "redistributable", "visual", "game", "games", "player", "media", "audio", "video", "sound", "office",
        "client", "manager", "center", "centre", "plus", "super", "easy", "smart", "cloud", "library", "pack",
        "framework", "helper", "host", "support", "utility", "utilities", "power", "my", "new", "best",
        "file", "files", "photo", "image", "music", "disk", "network", "security", "total",
    };

    private readonly HashSet<string> _nameKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exactKeys = new(StringComparer.Ordinal);
    private readonly List<string> _claimedPaths = new();
    private readonly List<string> _uninstalledNames = new();
    private readonly SafetyGuard _guard = KnownLocations.CreateGuard();
    private readonly List<string> _rootKeys = Roots.Select(r => SafetyGuard.NormalizePath(r.Root))
        .Append(SafetyGuard.NormalizePath(KnownLocations.UserProfile))
        .Append(SafetyGuard.NormalizePath(KnownLocations.LocalLow))
        .Where(r => r.Length > 0).ToList();

    private int _measured;
    private int _toMeasure;

    /// <summary>Emplacements examinés (premier niveau seulement). LocalLow est exclu : il contient surtout des sauvegardes de jeux.</summary>
    public static IReadOnlyList<(string Root, string Label)> Roots => new[]
    {
        (KnownLocations.ProgramFiles, "Program Files"),
        (KnownLocations.ProgramFilesX86, "Program Files (x86)"),
        (KnownLocations.ProgramData, "ProgramData"),
        (KnownLocations.RoamingAppData, "AppData\\Roaming"),
        (KnownLocations.LocalAppData, "AppData\\Local"),
        (KnownLocations.LocalPrograms, "AppData\\Local\\Programs"),
    }.Where(r => !string.IsNullOrEmpty(r.Item1))
     .DistinctBy(r => SafetyGuard.NormalizePath(r.Item1))
     .ToList();

    /// <summary>Analyse complète. Renvoie aussi les petits dossiers : la page décide de les afficher ou non.</summary>
    public List<OrphanFolder> Scan(IProgress<string>? progress, CancellationToken token)
    {
        progress?.Report(L.T("Lecture des logiciels installés…"));
        var programs = ProgramInventory.Load();
        foreach (var program in programs)
            AddProgram(program);
        token.ThrowIfCancellationRequested();

        progress?.Report(L.T("Lecture des services, du démarrage et des programmes en cours…"));
        AddServices();
        AddRunKeys();
        AddRunningProcesses();
        AddClaimedPath(Journal.Folder);
        AddClaimedPath(AppContext.BaseDirectory);
        LoadUninstalledNames();
        token.ThrowIfCancellationRequested();

        progress?.Report(L.T("Recherche des dossiers sans logiciel…"));
        var candidates = new List<(string Path, string Name, string Root, string Label)>();
        foreach (var (root, label) in Roots)
        {
            foreach (var dir in SafeDirectories(root))
            {
                token.ThrowIfCancellationRequested();
                var name = System.IO.Path.GetFileName(dir);
                if (IsCandidate(dir, name))
                    candidates.Add((dir, name, root, label));
            }
        }

        _toMeasure = candidates.Count;
        _measured = 0;
        var results = new List<OrphanFolder>();
        var gate = new object();
        Parallel.ForEach(candidates,
            new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) },
            candidate =>
            {
                var item = Measure(candidate.Path, candidate.Name, candidate.Root, candidate.Label, token);
                var done = Interlocked.Increment(ref _measured);
                progress?.Report(L.F("Mesure des dossiers : {0} sur {1}  ·  {2}", done, _toMeasure, candidate.Name));
                if (item is not null)
                {
                    lock (gate)
                        results.Add(item);
                }
            });

        Journal.Write($"Restes oubliés : {results.Count} proposés sur {candidates.Count} candidats.");
        return results
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.SizeBytes)
            .ToList();
    }

    // ----- Ce qui est « utilisé » -----

    private void AddProgram(InstalledProgram program)
    {
        AddNameKey(program.DisplayName);
        var publisherCompact = NameMatcher.CompactPublisher(program.Publisher);
        if (publisherCompact.Length >= 3)
            _exactKeys.Add(publisherCompact);
        AddExactKey(program.Publisher);
        AddFirstWord(program.DisplayName);
        AddFirstWord(program.Publisher);

        // « Mozilla Firefox » : on retient aussi « Firefox ».
        var name = NameMatcher.Compact(program.DisplayName);
        if (publisherCompact.Length >= 3 && name.StartsWith(publisherCompact, StringComparison.Ordinal) && name.Length - publisherCompact.Length >= 3)
            _nameKeys.Add(name[publisherCompact.Length..]);

        AddClaimedPath(program.InstallLocation);
        foreach (var command in new[] { program.DisplayIconPath, program.UninstallString, program.QuietUninstallString })
        {
            var exe = ExtractExePath(command);
            if (exe is null || exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                continue;
            AddClaimedPath(System.IO.Path.GetDirectoryName(exe));
            AddExeName(exe);
        }
    }

    private void AddServices()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var services = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null)
                return;
            foreach (var name in services.GetSubKeyNames())
            {
                try
                {
                    using var key = services.OpenSubKey(name);
                    var exe = ExtractExePath(key?.GetValue("ImagePath") as string);
                    if (exe is null)
                        continue;
                    AddClaimedPath(System.IO.Path.GetDirectoryName(exe));
                    AddExeName(exe);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // Service illisible : on passe au suivant.
                }
            }
        }
        catch (Exception ex)
        {
            Journal.Write("Restes oubliés : services illisibles : " + ex.Message);
        }
    }

    private void AddRunKeys()
    {
        var places = new[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        };
        foreach (var (hive, path) in places)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(path);
                if (key is null)
                    continue;
                foreach (var valueName in key.GetValueNames())
                {
                    var exe = ExtractExePath(key.GetValue(valueName) as string);
                    if (exe is null)
                        continue;
                    AddClaimedPath(System.IO.Path.GetDirectoryName(exe));
                    AddExeName(exe);
                }
            }
            catch (Exception ex)
            {
                Journal.Write($"Restes oubliés : clé {path} illisible : " + ex.Message);
            }
        }
    }

    private void AddRunningProcesses()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            Journal.Write("Restes oubliés : liste des processus illisible : " + ex.Message);
            return;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    var compact = NameMatcher.Compact(name);
                    if (compact.Length >= 4)
                        _exactKeys.Add(compact);

                    var path = GetProcessPath(process.Id);
                    if (path is not null)
                        AddClaimedPath(System.IO.Path.GetDirectoryName(path));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Processus terminé entre-temps ou protégé.
                }
            }
        }
    }

    /// <summary>Logiciels déjà désinstallés avec Uninstallix (historique « Restes de logiciels »).</summary>
    private void LoadUninstalledNames()
    {
        foreach (var entry in CleanupHistory.Load().Where(e => e.Kind == "Restes de logiciels"))
            _uninstalledNames.AddRange(entry.Items.Where(i => !string.IsNullOrWhiteSpace(i)));
    }

    private void AddNameKey(string? text)
    {
        var c = NameMatcher.Compact(text);
        if (c.Length >= 3)
            _nameKeys.Add(c);
    }

    private void AddExactKey(string? text)
    {
        var c = NameMatcher.Compact(text);
        if (c.Length >= 3)
            _exactKeys.Add(c);
    }

    private void AddFirstWord(string? text)
    {
        var first = NameMatcher.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is { Length: >= 4 } && !VagueWords.Contains(first) && !NameMatcher.IsGenericFolderName(first))
            _exactKeys.Add(first);
    }

    private void AddExeName(string exePath)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(exePath);
        if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("setup", StringComparison.OrdinalIgnoreCase)
            || name.Equals("update", StringComparison.OrdinalIgnoreCase)
            || name.Equals("svchost", StringComparison.OrdinalIgnoreCase))
            return;
        var c = NameMatcher.Compact(name);
        if (c.Length >= 3)
            _exactKeys.Add(c);
    }

    private void AddClaimedPath(string? path)
    {
        var p = SafetyGuard.NormalizePath(path);
        if (p.Length <= 3)
            return;
        // Un chemin égal à une racine examinée (ou au-dessus, ex. un .exe posé directement dans Program Files)
        // ne désigne aucun dossier en particulier : il rendrait toute la racine « utilisée ».
        foreach (var root in _rootKeys)
        {
            if (root == p || root.StartsWith(p + "\\", StringComparison.Ordinal))
                return;
        }
        _claimedPaths.Add(p);
    }

    // ----- Tri des dossiers -----

    private bool IsCandidate(string path, string name)
    {
        if (name.StartsWith('.') || name.StartsWith("regid.", StringComparison.OrdinalIgnoreCase) || GuidName.IsMatch(name))
            return false;

        var compact = NameMatcher.Compact(name);
        if (compact.Length < 3 || NameMatcher.IsGenericFolderName(name) || ExcludedNames.Contains(compact)
            || compact.StartsWith("windows", StringComparison.Ordinal) || compact.StartsWith("microsoft", StringComparison.Ordinal))
            return false;

        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)
                || (attributes.HasFlag(FileAttributes.System) && attributes.HasFlag(FileAttributes.Hidden)))
                return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (_guard.CheckPath(path) is not null || Favorites.IsProtectedPath(path) || Favorites.ContainsProtectedPath(path))
            return false;
        if (IsClaimedPath(path) || IsUsedName(compact))
            return false;

        // Dossier d'éditeur : s'il contient un sous-dossier au nom d'un logiciel installé, il sert encore.
        foreach (var sub in SafeDirectories(path))
        {
            var subCompact = NameMatcher.Compact(System.IO.Path.GetFileName(sub));
            if (subCompact.Length >= 3 && (_nameKeys.Contains(subCompact) || _exactKeys.Contains(subCompact)))
                return false;
        }
        return true;
    }

    private bool IsClaimedPath(string path)
    {
        var p = SafetyGuard.NormalizePath(path);
        foreach (var claimed in _claimedPaths)
        {
            if (claimed == p
                || claimed.StartsWith(p + "\\", StringComparison.Ordinal)
                || p.StartsWith(claimed + "\\", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Comparaison souple : casse, espaces, ponctuation, versions et préfixes communs.</summary>
    private bool IsUsedName(string compact)
    {
        if (_nameKeys.Contains(compact) || _exactKeys.Contains(compact))
            return true;
        foreach (var key in _nameKeys)
        {
            if (compact.Length >= 4 && key.StartsWith(compact, StringComparison.Ordinal))
                return true;
            if (key.Length >= 4 && compact.StartsWith(key, StringComparison.Ordinal))
                return true;
            if ((key.Length >= 6 && compact.Contains(key, StringComparison.Ordinal))
                || (compact.Length >= 6 && key.Contains(compact, StringComparison.Ordinal)))
                return true;
        }
        foreach (var key in _exactKeys)
        {
            if (key.Length >= 5 && compact.StartsWith(key, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ----- Mesure et confiance -----

    private OrphanFolder? Measure(string path, string name, string root, string label, CancellationToken token)
    {
        long size = 0;
        var count = 0;
        var partial = false;
        var hasPrograms = false;
        DateTime latest;
        try
        {
            latest = Directory.GetLastWriteTimeUtc(path);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            {
                if ((count & 0x3FF) == 0)
                    token.ThrowIfCancellationRequested();
                if (entry.LastWriteTimeUtc > latest)
                    latest = entry.LastWriteTimeUtc;
                if (entry is FileInfo file)
                {
                    size += file.Length;
                    if (!hasPrograms && (file.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                                         || file.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                                         || file.Extension.Equals(".sys", StringComparison.OrdinalIgnoreCase)))
                        hasPrograms = true;
                }
                if (++count >= MaxFilesPerFolder)
                {
                    partial = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Journal.Write($"Restes oubliés : mesure impossible de {path} : {ex.Message}");
            return null;
        }

        var lastModified = latest.ToLocalTime();
        var ageDays = (int)(DateTime.Now - lastModified).TotalDays;
        if (ageDays < MinAgeDays)
            return null;

        var (confidence, reason) = Rate(name, root, hasPrograms, ageDays);
        return new OrphanFolder
        {
            Path = path,
            Name = name,
            Location = label,
            SizeBytes = size,
            SizeIsPartial = partial,
            LastModified = lastModified,
            HasPrograms = hasPrograms,
            Confidence = confidence,
            Reason = reason,
        };
    }

    private (OrphanConfidence, string) Rate(string name, string root, bool hasPrograms, int ageDays)
    {
        var uninstalled = _uninstalledNames.FirstOrDefault(n => NameMatcher.Match(name, n, null) == MatchConfidence.High);
        if (uninstalled is not null)
            return (OrphanConfidence.Safe, L.F("Reste de « {0} », désinstallé avec Uninstallix", uninstalled));

        var inProgramFiles = root.Equals(KnownLocations.ProgramFiles, StringComparison.OrdinalIgnoreCase)
                             || root.Equals(KnownLocations.ProgramFilesX86, StringComparison.OrdinalIgnoreCase);
        if (inProgramFiles && !hasPrograms && ageDays >= 180)
            return (OrphanConfidence.Safe, L.T("Aucun programme dedans et aucun logiciel installé ne l'utilise"));

        if (hasPrograms && ageDays < 365)
            return (OrphanConfidence.Check, L.T("Contient des programmes : peut-être un logiciel portable"));

        if (ageDays >= 180)
            return (OrphanConfidence.Likely, L.F("Aucun logiciel installé ne l'utilise, inchangé depuis {0} mois", ageDays / 30));

        return (OrphanConfidence.Check, L.F("Aucun logiciel installé ne l'utilise, mais modifié il y a {0} jours", ageDays));
    }

    // ----- Outils -----

    /// <summary>Extrait le chemin d'un .exe d'une ligne de commande ou d'une icône (« "C:\…\app.exe" /x », « C:\…\app.exe,0 »).</summary>
    public static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var s = Environment.ExpandEnvironmentVariables(command.Trim());
        if (s.StartsWith(@"\??\", StringComparison.Ordinal))
            s = s[4..];

        string candidate;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            candidate = end > 1 ? s[1..end] : s.Trim('"');
        }
        else
        {
            var exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exe > 0 ? s[..(exe + 4)] : s.Split(',')[0];
        }

        candidate = candidate.Trim();
        var comma = candidate.LastIndexOf(',');
        if (comma > 2 && !candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[..comma];
        return candidate.Length > 3 && candidate[1] == ':' && candidate[2] == '\\' ? candidate : null;
    }

    private static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Chemin de l'exécutable d'un processus, sans lire ses modules (rapide, marche pour les processus 32 bits).</summary>
    private static string? GetProcessPath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
