using System.Runtime.InteropServices;
using Desinstalleur.App.Models;

namespace Desinstalleur.App.Services;

/// <summary>
/// Gros fichiers oubliés dans Téléchargements et sur le Bureau (premier niveau et un sous-dossier) :
/// installeurs et archives de plus de 30 jours, images disque, et fichiers de plus de 500 Mo
/// inutilisés depuis 90 jours. Rien n'est coché d'office.
/// </summary>
public static class ForgottenFiles
{
    public const long LargeBytes = 500L * 1024 * 1024;
    public const int OldDays = 30;
    public const int UnusedDays = 90;

    private static readonly HashSet<string> InstallerExt = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".msix", ".msixbundle", ".appx" };
    private static readonly HashSet<string> ArchiveExt = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz" };
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".iso", ".img" };

    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>Dossier Téléchargements réel (il peut avoir été déplacé), sinon %UserProfile%\Downloads.</summary>
    public static string DownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var ptr) == 0)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public static List<ForgottenFile> Scan(IProgress<string> progress, CancellationToken token)
    {
        var roots = new List<(string Path, string Label)>
        {
            (DownloadsFolder(), "Téléchargements"),
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Bureau"),
        };
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 1,
            IgnoreInaccessible = true,
            AttributesToSkip = DuplicateFinder.Skipped,
            ReturnSpecialDirectories = false,
        };

        var now = DateTime.Now;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<ForgottenFile>();
        foreach (var (root, label) in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root) || Favorites.IsProtectedPath(root))
                continue;
            progress.Report(L.F("Examen de {0}…", root));
            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(root).EnumerateFiles("*", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Journal.Write($"Gros fichiers oubliés : dossier illisible {root} : {ex.Message}");
                continue;
            }

            foreach (var info in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((info.Attributes & DuplicateFinder.Skipped) != 0 || !seen.Add(info.FullName) || Favorites.IsProtectedPath(info.FullName))
                        continue;
                    var ext = info.Extension;
                    var length = info.Length;
                    var written = info.LastWriteTime > info.CreationTime ? info.LastWriteTime : info.CreationTime;
                    var used = info.LastAccessTime > written ? info.LastAccessTime : written;
                    var oldEnough = (now - written).TotalDays >= OldDays;

                    ForgottenKind? kind = null;
                    if (InstallerExt.Contains(ext) && oldEnough)
                        kind = ForgottenKind.Installer;
                    else if (ArchiveExt.Contains(ext) && oldEnough)
                        kind = ForgottenKind.Archive;
                    else if (ImageExt.Contains(ext))
                        kind = ForgottenKind.DiskImage;
                    else if (length >= LargeBytes && (now - used).TotalDays >= UnusedDays)
                        kind = ForgottenKind.Large;
                    if (kind is null)
                        continue;

                    found.Add(new ForgottenFile
                    {
                        Path = info.FullName,
                        Kind = kind.Value,
                        SizeBytes = length,
                        LastModified = info.LastWriteTime,
                        LastUsed = kind == ForgottenKind.Large ? used : written,
                        Location = label,
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Journal.Write($"Gros fichiers oubliés : {info.FullName} illisible : {ex.Message}");
                }
            }
        }
        Journal.Write($"Gros fichiers oubliés : {found.Count} fichiers, {found.Sum(f => f.SizeBytes)} octets.");
        return found.OrderByDescending(f => f.SizeBytes).ToList();
    }
}
