using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

/// <summary>Résumé compact d'une analyse de l'espace disque, gardé pour la comparer à la suivante.</summary>
public sealed class DiskSnapshot
{
    public DateTime Date { get; set; }
    public string Root { get; set; } = string.Empty;
    public long TotalBytes { get; set; }

    /// <summary>Taille du plus petit dossier enregistré : un dossier absent était plus petit (ou n'existait pas).</summary>
    public long MinKeptBytes { get; set; }

    /// <summary>Chemin complet du dossier → taille en octets.</summary>
    public Dictionary<string, long> Folders { get; set; } = new();
}

/// <summary>Un dossier qui a grossi depuis l'analyse précédente.</summary>
public sealed class GrowthRow
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long BeforeBytes { get; init; }
    public long AfterBytes { get; init; }
    public long DeltaBytes { get; init; }
    public bool IsNew { get; init; }

    /// <summary>Vrai quand le dossier n'était pas dans l'analyse précédente (trop petit pour être noté).</summary>
    public bool BeforeUnknown { get; init; }

    public bool IsReal => true;
    public string Detail => Path.GetDirectoryName(FullPath) ?? FullPath;
    public string BeforeAfterText => IsNew
        ? ValueParsers.FormatSize(AfterBytes)
        : (BeforeUnknown ? L.F("moins de {0}", ValueParsers.FormatSize(BeforeBytes)) : ValueParsers.FormatSize(BeforeBytes))
          + "  →  " + ValueParsers.FormatSize(AfterBytes);
    public string DeltaText => "+" + ValueParsers.FormatSize(DeltaBytes);
}

/// <summary>Résultat de la comparaison entre l'analyse qui vient de finir et la précédente du même emplacement.</summary>
public sealed class DiskComparison
{
    public required DateTime PreviousDate { get; init; }
    public required long PreviousTotal { get; init; }
    public required long CurrentTotal { get; init; }
    public required List<GrowthRow> Grown { get; init; }

    public long Delta => CurrentTotal - PreviousTotal;

    public string DateText
    {
        get
        {
            var date = PreviousDate.Date;
            if (date == DateTime.Today)
                return L.F("Depuis {0} aujourd'hui", PreviousDate.ToString(L.IsEnglish ? "h:mm tt" : "HH:mm", L.Culture));
            var format = date.Year == DateTime.Today.Year
                ? (L.IsEnglish ? "MMM d" : "d MMM")
                : (L.IsEnglish ? "MMM d, yyyy" : "d MMM yyyy");
            return L.F("Depuis le {0}", PreviousDate.ToString(format, L.Culture));
        }
    }

    public string SummaryText
    {
        get
        {
            if (Math.Abs(Delta) < DiskSnapshots.UnchangedBytes)
                return L.F("{0} : presque aucun changement", DateText);
            var size = ValueParsers.FormatSize(Math.Abs(Delta));
            return Delta > 0 ? L.F("{0} : +{1}", DateText, size) : L.F("{0} : −{1}", DateText, size);
        }
    }
}

/// <summary>
/// Garde la dernière analyse de chaque emplacement dans Journal.Folder\analyses, pour montrer ce qui a grossi
/// à l'analyse suivante. Lecture seule sur le disque analysé : seul le petit fichier de l'analyse est écrit.
/// </summary>
public static class DiskSnapshots
{
    /// <summary>Nombre maximal de dossiers notés (les plus gros) pour que le fichier reste petit.</summary>
    private const int MaxFolders = 4000;

    /// <summary>En dessous, un dossier qui a grossi n'est pas montré dans « Ce qui a grossi ».</summary>
    public const long MinGrowthBytes = 50L << 20;

    /// <summary>Écart total en dessous duquel on parle de « presque aucun changement ».</summary>
    public const long UnchangedBytes = 1L << 20;

    /// <summary>Un dossier est masqué quand un de ses sous-dossiers explique déjà cette part de sa croissance.</summary>
    private const double ExplainedByChild = 0.8;

    public static string Folder => Path.Combine(Journal.Folder, "analyses");

    /// <summary>
    /// Compare l'analyse terminée à la précédente du même emplacement, puis la garde à sa place.
    /// Renvoie null pour une toute première analyse (ou si la précédente est illisible).
    /// </summary>
    public static DiskComparison? CompareAndSave(DiskNode root)
    {
        DiskSnapshot current;
        try
        {
            current = SnapshotOf(root);
        }
        catch (Exception ex)
        {
            Journal.Write("Résumé de l'analyse impossible : " + ex.Message);
            return null;
        }
        var path = FileFor(current.Root);
        var previous = Load(path);
        DiskComparison? comparison = null;
        if (previous is not null)
        {
            try
            {
                comparison = Compare(previous, current);
            }
            catch (Exception ex)
            {
                Journal.Write("Comparaison avec l'analyse précédente impossible : " + ex.Message);
            }
        }
        Save(path, current);
        return comparison;
    }

    private static DiskSnapshot SnapshotOf(DiskNode root)
    {
        var all = DiskScanner.FoldersUnder(root).OrderByDescending(f => f.SizeBytes).ToList();
        var kept = all.Take(MaxFolders).ToList();
        var snapshot = new DiskSnapshot
        {
            Date = DateTime.Now,
            Root = Normalize(root.FullPath),
            TotalBytes = root.SizeBytes,
            MinKeptBytes = all.Count > MaxFolders && kept.Count > 0
                ? Math.Max(DiskScanner.KeepFolderBytes, kept[^1].SizeBytes)
                : DiskScanner.KeepFolderBytes,
        };
        foreach (var folder in kept)
            snapshot.Folders[Normalize(folder.FullPath)] = folder.SizeBytes;
        return snapshot;
    }

    private static DiskComparison Compare(DiskSnapshot previous, DiskSnapshot current)
    {
        var before = new Dictionary<string, long>(previous.Folders, StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, GrowthRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var (folderPath, after) in current.Folders)
        {
            if (after < MinGrowthBytes)
                continue;
            GrowthRow row;
            if (before.TryGetValue(folderPath, out var old))
            {
                if (after - old < MinGrowthBytes)
                    continue;
                row = MakeRow(folderPath, old, after, isNew: false, unknown: false);
            }
            else if (CreatedAfter(folderPath, previous.Date))
            {
                row = MakeRow(folderPath, 0, after, isNew: true, unknown: false);
            }
            else
            {
                // Absent de l'analyse précédente : il pesait au plus MinKeptBytes (estimation prudente).
                var bound = Math.Max(0, previous.MinKeptBytes);
                if (after - bound < MinGrowthBytes)
                    continue;
                row = MakeRow(folderPath, bound, after, isNew: false, unknown: true);
            }
            candidates[folderPath] = row;
        }

        // Un parent qui a grossi surtout à cause d'un de ses sous-dossiers est masqué : on montre le plus précis.
        var explained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in candidates.Values)
        {
            var parent = Path.GetDirectoryName(row.FullPath);
            if (parent is not null && candidates.TryGetValue(parent, out var parentRow) &&
                row.DeltaBytes >= parentRow.DeltaBytes * ExplainedByChild)
                explained.Add(parent);
        }

        return new DiskComparison
        {
            PreviousDate = previous.Date,
            PreviousTotal = previous.TotalBytes,
            CurrentTotal = current.TotalBytes,
            Grown = candidates.Values
                .Where(r => !explained.Contains(r.FullPath))
                .OrderByDescending(r => r.DeltaBytes)
                .ToList(),
        };
    }

    private static GrowthRow MakeRow(string folderPath, long before, long after, bool isNew, bool unknown) => new()
    {
        Name = Path.GetFileName(folderPath) is { Length: > 0 } name ? name : folderPath,
        FullPath = folderPath,
        BeforeBytes = before,
        AfterBytes = after,
        DeltaBytes = after - before,
        IsNew = isNew,
        BeforeUnknown = unknown,
    };

    private static bool CreatedAfter(string folderPath, DateTime date)
    {
        try
        {
            return Directory.Exists(folderPath) && Directory.GetCreationTime(folderPath) > date;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static DiskSnapshot? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var snapshot = JsonSerializer.Deserialize<DiskSnapshot>(File.ReadAllText(path));
            return snapshot is { Folders: not null } ? snapshot : null;
        }
        catch (Exception ex)
        {
            Journal.Write("Analyse précédente illisible (" + path + ") : " + ex.Message);
            return null;
        }
    }

    private static void Save(string path, DiskSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Journal.Write("Enregistrement de l'analyse impossible (" + path + ") : " + ex.Message);
        }
    }

    private static string Normalize(string folderPath)
    {
        var full = Path.GetFullPath(folderPath);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // « C:\ » garde son séparateur ; les autres chemins n'en ont pas à la fin.
        return trimmed.EndsWith(':') || trimmed.Length == 0 ? full : trimmed;
    }

    /// <summary>Nom de fichier lisible et sans collision pour un emplacement analysé.</summary>
    private static string FileFor(string root)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var readable = new StringBuilder();
        foreach (var c in root)
            readable.Append(invalid.Contains(c) || c is ':' or '\\' or '/' ? '_' : c);
        var name = readable.ToString().Trim('_');
        if (name.Length > 60)
            name = name[..60];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..8].ToLowerInvariant();
        return Path.Combine(Folder, (name.Length > 0 ? name : "racine") + "-" + hash + ".json");
    }
}
