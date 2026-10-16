using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

/// <summary>Un dossier ou un gros fichier dans l'analyse de l'espace disque.</summary>
public sealed class DiskNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }

    /// <summary>Sous-éléments assez gros pour être détaillés (null pour un petit dossier).</summary>
    public List<DiskNode>? Children { get; set; }

    /// <summary>Taille cumulée des petits fichiers non détaillés.</summary>
    public long SmallFilesBytes { get; set; }
    public int SmallFilesCount { get; set; }

    /// <summary>Faux pour la ligne « Petits fichiers » (ni ouvrable ni explorable).</summary>
    public bool IsReal { get; init; } = true;

    public bool CanOpen => IsFolder && Children is { Count: > 0 };
    public double Percent { get; set; }
    public string Glyph => !IsReal ? "" : IsFolder ? "" : "";
    public string SizeText => ValueParsers.FormatSize(SizeBytes);
    public string PercentText => Percent >= 0.1 ? L.F("{0:0.#} %", Percent) : L.T("< 0,1 %");
    public string Detail => IsFolder ? L.F(FileCount > 1 ? "{0:N0} fichiers" : "{0:N0} fichier", FileCount)
        : !IsReal ? L.F(FileCount > 1 ? "{0:N0} fichiers de moins de 25 Mo" : "{0:N0} fichier de moins de 25 Mo", FileCount)
        : L.T("Fichier");
}

/// <summary>
/// Mesure ce qui prend de la place dans un dossier ou un disque.
/// Lecture seule : rien n'est jamais supprimé ici. Les liens et jonctions ne sont pas suivis.
/// </summary>
public sealed class DiskScanner
{
    private const long KeepFolderBytes = 1L << 20;      // dossiers détaillés à partir de 1 Mo
    private const long KeepFileBytes = 25L << 20;       // fichiers listés à partir de 25 Mo

    private long _files;
    private long _bytes;

    public long FilesScanned => Interlocked.Read(ref _files);
    public long BytesScanned => Interlocked.Read(ref _bytes);
    public string CurrentFolder { get; private set; } = string.Empty;

    public DiskNode Scan(string root, CancellationToken cancel)
    {
        var info = new DirectoryInfo(root);
        var node = new DiskNode { Name = root, FullPath = info.FullName, IsFolder = true };
        var children = new System.Collections.Concurrent.ConcurrentBag<DiskNode>();
        long smallBytes = 0, smallCount = 0;

        List<FileSystemInfo> entries;
        try
        {
            entries = info.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries = new List<FileSystemInfo>();
        }

        // Premier niveau en parallèle : chaque gros dossier est mesuré dans son coin.
        Parallel.ForEach(entries, new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = 4 }, entry =>
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return;
            if (entry is DirectoryInfo dir)
            {
                var child = ScanFolder(dir, cancel);
                children.Add(child);
            }
            else if (entry is FileInfo file)
            {
                var length = SafeLength(file);
                Interlocked.Increment(ref _files);
                Interlocked.Add(ref _bytes, length);
                if (length >= KeepFileBytes)
                    children.Add(FileNode(file, length));
                else
                {
                    Interlocked.Add(ref smallBytes, length);
                    Interlocked.Increment(ref smallCount);
                }
            }
        });

        node.Children = children.ToList();
        node.SmallFilesBytes = smallBytes;
        node.SmallFilesCount = (int)smallCount;
        node.SizeBytes = node.Children.Sum(c => c.SizeBytes) + smallBytes;
        node.FileCount = node.Children.Sum(c => c.FileCount) + (int)smallCount;
        return node;
    }

    private DiskNode ScanFolder(DirectoryInfo dir, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        CurrentFolder = dir.FullName;
        var node = new DiskNode { Name = dir.Name, FullPath = dir.FullName, IsFolder = true };
        var children = new List<DiskNode>();
        long size = 0, smallBytes = 0;
        int count = 0, smallCount = 0;

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return node;
        }

        try
        {
            foreach (var entry in entries)
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                if (entry is DirectoryInfo sub)
                {
                    var child = ScanFolder(sub, cancel);
                    size += child.SizeBytes;
                    count += child.FileCount;
                    if (child.SizeBytes >= KeepFolderBytes)
                        children.Add(child);
                    else
                    {
                        smallBytes += child.SizeBytes;
                        smallCount += child.FileCount;
                    }
                }
                else if (entry is FileInfo file)
                {
                    var length = SafeLength(file);
                    Interlocked.Increment(ref _files);
                    Interlocked.Add(ref _bytes, length);
                    size += length;
                    count++;
                    if (length >= KeepFileBytes)
                        children.Add(FileNode(file, length));
                    else
                    {
                        smallBytes += length;
                        smallCount++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // dossier devenu illisible pendant l'analyse : on garde ce qui a été compté
        }

        node.SizeBytes = size;
        node.FileCount = count;
        node.Children = children.Count > 0 ? children : null;
        node.SmallFilesBytes = smallBytes;
        node.SmallFilesCount = smallCount;
        return node;
    }

    private static DiskNode FileNode(FileInfo file, long length) =>
        new() { Name = file.Name, FullPath = file.FullName, IsFolder = false, SizeBytes = length, FileCount = 1 };

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Contenu à afficher d'un dossier analysé : sous-éléments triés, plus une ligne « petits fichiers ».</summary>
    public static List<DiskNode> RowsOf(DiskNode folder)
    {
        var rows = (folder.Children ?? new List<DiskNode>()).OrderByDescending(c => c.SizeBytes).ToList();
        if (folder.SmallFilesBytes > 0)
        {
            rows.Add(new DiskNode
            {
                Name = L.T("Petits fichiers et dossiers"),
                FullPath = folder.FullPath,
                IsFolder = false,
                IsReal = false,
                SizeBytes = folder.SmallFilesBytes,
                FileCount = folder.SmallFilesCount,
            });
        }
        var total = Math.Max(1, folder.SizeBytes);
        foreach (var row in rows)
            row.Percent = row.SizeBytes * 100.0 / total;
        return rows;
    }
}
