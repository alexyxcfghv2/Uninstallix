using System.Collections.Concurrent;
using System.Security.Cryptography;
using Desinstalleur.App.Models;
using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

/// <summary>Avancement d'une recherche de doublons (Fraction &lt; 0 : avancement inconnu).</summary>
public readonly record struct DuplicateProgress(string Text, double Fraction);

/// <summary>
/// Cherche les fichiers identiques : regroupement par taille, puis empreinte partielle (début et fin),
/// puis SHA-256 complet lu en continu. Les fichiers cachés, système, les jonctions et les fichiers
/// OneDrive « à la demande » (non téléchargés) sont ignorés : rien n'est jamais téléchargé.
/// </summary>
public sealed class DuplicateFinder
{
    private const int PartialBytes = 64 * 1024;
    private const int BufferBytes = 1024 * 1024;

    // Attributs absents de l'énumération FileAttributes de .NET.
    internal const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    internal const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>Fichiers jamais examinés : cachés, système, liens, hors ligne ou dans le cloud seulement.</summary>
    internal const FileAttributes Skipped = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint |
                                            FileAttributes.Offline | FileAttributes.Temporary | RecallOnOpen | RecallOnDataAccess;

    /// <summary>Dossiers proposés par défaut : Images, Vidéos, Téléchargements, Documents, Bureau.</summary>
    public static List<string> DefaultFolders()
    {
        var list = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            ForgottenFiles.DownloadsFolder(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        return list.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<DuplicateGroup> Scan(IReadOnlyList<string> folders, long minSize, IProgress<DuplicateProgress> progress,
        CancellationToken token)
    {
        // 1. Liste des fichiers, regroupés par taille.
        progress.Report(new DuplicateProgress(L.T("Recherche des fichiers…"), -1));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bySize = new Dictionary<long, List<DuplicateFile>>();
        var count = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = Skipped,
            ReturnSpecialDirectories = false,
        };

        foreach (var folder in folders)
        {
            if (Favorites.IsProtectedPath(folder) || !Directory.Exists(folder))
                continue;
            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(folder).EnumerateFiles("*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Journal.Write($"Doublons : dossier illisible {folder} : {ex.Message}");
                continue;
            }

            using var e = files.GetEnumerator();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                FileInfo info;
                try
                {
                    if (!e.MoveNext())
                        break;
                    info = e.Current;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Journal.Write($"Doublons : parcours interrompu dans {folder} : {ex.Message}");
                    break;
                }

                long length;
                DateTime modified;
                try
                {
                    if ((info.Attributes & Skipped) != 0)
                        continue;
                    length = info.Length;
                    modified = info.LastWriteTime;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (length < minSize || length <= 0 || !seen.Add(info.FullName) || Favorites.IsProtectedPath(info.FullName))
                    continue;

                if (!bySize.TryGetValue(length, out var list))
                    bySize[length] = list = new List<DuplicateFile>(2);
                list.Add(new DuplicateFile { Path = info.FullName, SizeBytes = length, LastModified = modified });
                if (++count % 500 == 0)
                    progress.Report(new DuplicateProgress(L.F("{0} fichiers trouvés…", count), -1));
            }
        }

        var candidates = bySize.Values.Where(l => l.Count > 1).ToList();
        bySize.Clear();
        Journal.Write($"Doublons : {count} fichiers parcourus, {candidates.Sum(c => c.Count)} de même taille.");

        // 2. Empreinte partielle (64 Ko au début + 64 Ko à la fin). Les petits fichiers sont lus en entier.
        var partialTotal = candidates.Sum(c => c.Count);
        var done = 0;
        var partialKeys = new ConcurrentDictionary<DuplicateFile, string>();
        var parallel = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 4) };
        Parallel.ForEach(candidates.SelectMany(c => c), parallel, file =>
        {
            var key = PartialHash(file.Path, file.SizeBytes, token);
            if (key is not null)
                partialKeys[file] = key;
            var n = Interlocked.Increment(ref done);
            if (n % 50 == 0)
                progress.Report(new DuplicateProgress(L.F("Comparaison rapide : {0} sur {1}", n, partialTotal), 0.3 * n / partialTotal));
        });

        var stage2 = candidates
            .SelectMany(c => c.Where(partialKeys.ContainsKey).GroupBy(f => partialKeys[f]))
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

        // 3. SHA-256 complet (inutile pour les fichiers déjà lus en entier à l'étape 2).
        var fullKeys = new ConcurrentDictionary<DuplicateFile, string>();
        var toHash = stage2.Where(g => g[0].SizeBytes > 2 * PartialBytes).SelectMany(g => g).ToList();
        foreach (var g in stage2.Where(g => g[0].SizeBytes <= 2 * PartialBytes))
            foreach (var f in g)
                fullKeys[f] = partialKeys[f];

        long totalBytes = toHash.Sum(f => f.SizeBytes);
        long hashedBytes = 0;
        var lastReport = Environment.TickCount64;
        Parallel.ForEach(toHash, parallel, file =>
        {
            var hash = FullHash(file.Path, token, read =>
            {
                var now = Interlocked.Add(ref hashedBytes, read);
                var tick = Environment.TickCount64;
                if (tick - Interlocked.Read(ref lastReport) > 150)
                {
                    Interlocked.Exchange(ref lastReport, tick);
                    progress.Report(new DuplicateProgress(
                        L.F("Vérification du contenu : {0} sur {1}", ValueParsers.FormatSize(now), ValueParsers.FormatSize(totalBytes)),
                        0.3 + 0.7 * now / Math.Max(1, totalBytes)));
                }
            });
            if (hash is not null)
                fullKeys[file] = hash;
        });

        var groups = stage2
            .SelectMany(g => g.Where(fullKeys.ContainsKey).GroupBy(f => fullKeys[f]))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup(g.First().SizeBytes, g.Key, g.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(g => g.WastedBytes)
            .ToList();
        Journal.Write($"Doublons : {groups.Count} groupes, {groups.Sum(g => g.WastedBytes)} octets récupérables.");
        return groups;
    }

    private static FileStream Open(string path) =>
        new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.SequentialScan,
            BufferSize = 0,
        });

    private static string? PartialHash(string path, long size, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            using var stream = Open(path);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[PartialBytes];
            if (size <= 2 * PartialBytes)
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    sha.AppendData(buffer, 0, read);
            }
            else
            {
                var read = stream.ReadAtLeast(buffer, PartialBytes, throwOnEndOfStream: false);
                sha.AppendData(buffer, 0, read);
                stream.Seek(-PartialBytes, SeekOrigin.End);
                read = stream.ReadAtLeast(buffer, PartialBytes, throwOnEndOfStream: false);
                sha.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Journal.Write($"Doublons : lecture impossible de {path} : {ex.Message}");
            return null;
        }
    }

    private static string? FullHash(string path, CancellationToken token, Action<int> onRead)
    {
        try
        {
            using var stream = Open(path);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferBytes];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
                onRead(read);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Journal.Write($"Doublons : lecture impossible de {path} : {ex.Message}");
            return null;
        }
    }

    private static readonly HashSet<string> PhotoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".heic", ".heif", ".webp", ".tif", ".tiff", ".raw", ".cr2", ".nef", ".arw", ".dng" };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".webm", ".mts", ".m2ts", ".3gp" };
    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".opus" };
    private static readonly HashSet<string> ArchiveExt = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso", ".img" };
    private static readonly HashSet<string> ProgramExt = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".msi", ".msix", ".appx", ".dll" };

    /// <summary>Icône Segoe adaptée à l'extension.</summary>
    public static string GlyphFor(string path)
    {
        var ext = Path.GetExtension(path);
        if (PhotoExt.Contains(ext)) return "";
        if (VideoExt.Contains(ext)) return "";
        if (AudioExt.Contains(ext)) return "";
        if (ArchiveExt.Contains(ext)) return "";
        if (ProgramExt.Contains(ext)) return "";
        return "";
    }
}

/// <summary>
/// Suppression d'un fichier de l'utilisateur choisi à l'écran « Fichiers » : revérifie le chemin
/// (SafetyGuard, favoris protégés, liens, fichiers dans le cloud) puis l'envoie à la Corbeille,
/// ou le supprime définitivement si la Corbeille est désactivée dans les réglages.
/// </summary>
public static class UserFileRemover
{
    private static readonly Lazy<SafetyGuard> Guard = new(KnownLocations.CreateGuard);

    /// <summary>Renvoie (réussi, texte affiché). <paramref name="expectedSize"/> : taille vue à l'analyse (le fichier ne doit pas avoir changé).</summary>
    public static (bool Ok, string Message) Remove(string path, long expectedSize)
    {
        try
        {
            var refusal = Guard.Value.CheckPath(path);
            if (refusal is not null)
                return (false, L.T(refusal));
            if (Favorites.IsProtectedPath(path))
                return (false, L.T("Protégé par vos favoris : conservé."));

            var info = new FileInfo(path);
            if (!info.Exists)
                return (true, L.T("Déjà absent"));
            if ((info.Attributes & (FileAttributes.ReparsePoint | DuplicateFinder.RecallOnOpen | DuplicateFinder.RecallOnDataAccess | FileAttributes.Offline)) != 0)
                return (false, L.T("Lien ou fichier en ligne seulement : ignoré par sécurité."));
            if (info.Length != expectedSize)
                return (false, L.T("Le fichier a changé depuis l'analyse : conservé."));

            if (!AppSettings.Current.UseRecycleBin)
            {
                if (info.IsReadOnly)
                    info.IsReadOnly = false;
                info.Delete();
                Journal.Write($"Fichiers : supprimé définitivement {path}");
                return (true, L.T("Supprimé définitivement"));
            }

            var code = NativeMethods.SendToRecycleBin(path);
            if (code == 0 && !File.Exists(path))
            {
                Journal.Write($"Fichiers : envoyé à la Corbeille {path}");
                return (true, L.T("Envoyé à la Corbeille"));
            }
            Journal.Write($"Fichiers : échec de la suppression de {path} (code {code})");
            return (false, code == 0 ? L.T("Fichier utilisé ou protégé.") : L.F("Erreur Windows {0}.", code));
        }
        catch (Exception ex)
        {
            Journal.Write($"Fichiers : suppression impossible de {path} : {ex.Message}");
            return (false, ex.Message);
        }
    }
}
