using Desinstalleur.Core;

namespace Desinstalleur.App.Models;

/// <summary>Un fichier ou sous-dossier qui sera supprimé par le nettoyage.</summary>
public sealed class PreviewEntry
{
    public PreviewEntry(string name, string path, long sizeBytes, int fileCount, bool isFolder)
    {
        Name = name;
        Path = path;
        SizeBytes = sizeBytes;
        FileCount = fileCount;
        IsFolder = isFolder;
    }

    public string Name { get; }
    public string Path { get; }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsFolder { get; }

    public string Glyph => IsFolder ? "" : "";
    public string SizeText => ValueParsers.FormatSize(SizeBytes);
    public string Detail => IsFolder ? $"{FileCount} fichier(s)  ·  {Path}" : Path;
}

/// <summary>Aperçu complet d'un cache avant nettoyage.</summary>
public sealed class CachePreview
{
    private const int MaxShown = 150;

    public CachePreview(CacheTarget target) => Target = target;

    public CacheTarget Target { get; }
    public List<PreviewEntry> Entries { get; } = new();

    /// <summary>Fichiers conservés : récents, protégés ou dans un sous-dossier gardé.</summary>
    public int KeptFiles { get; set; }

    public string Name => Target.Name;
    public string Glyph => Target.Glyph;
    public long TotalBytes => Entries.Sum(e => e.SizeBytes);
    public int TotalFiles => Entries.Sum(e => e.FileCount);
    public string TotalText => ValueParsers.FormatSize(TotalBytes);
    public bool IsEmpty => Entries.Count == 0;

    public IEnumerable<PreviewEntry> ShownEntries => Entries.Take(MaxShown);

    public string SummaryText
    {
        get
        {
            if (IsEmpty)
                return "Rien à supprimer";
            var text = $"{TotalFiles} fichier(s)";
            if (Entries.Count > MaxShown)
                text += $"  ·  {MaxShown} plus gros éléments affichés sur {Entries.Count}";
            if (KeptFiles > 0)
                text += $"  ·  {KeptFiles} fichier(s) conservé(s) (récents ou protégés)";
            return text;
        }
    }
}
