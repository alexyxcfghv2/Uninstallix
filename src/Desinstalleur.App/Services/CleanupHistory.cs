using System.Text.Json;
using System.Text.Json.Serialization;
using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

/// <summary>Une ligne de l'historique : quand, quoi, et l'espace réellement récupéré.</summary>
public sealed class HistoryEntry
{
    public DateTime Date { get; set; }
    public string Kind { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
    public long FreedBytes { get; set; }
    public int Errors { get; set; }

    [JsonIgnore]
    public string KindText => L.T(Kind);
    [JsonIgnore]
    public string DateText => Date.ToString(L.IsEnglish ? "dddd, MMMM d, yyyy  ·  h:mm tt" : "dddd d MMMM yyyy  ·  HH:mm", L.Culture);
    [JsonIgnore]
    public string FreedText => ValueParsers.FormatSize(FreedBytes);
    [JsonIgnore]
    public string ItemsText => string.Join(", ", Items.Take(6).Select(L.T))
        + (Items.Count > 6 ? L.F(Items.Count - 6 > 1 ? " et {0} autres" : " et {0} autre", Items.Count - 6) : string.Empty);
    [JsonIgnore]
    public string ErrorsText => Errors == 0 ? string.Empty : L.F(Errors > 1 ? "{0} éléments non supprimés" : "{0} élément non supprimé", Errors);
    [JsonIgnore]
    public bool HasErrors => Errors > 0;
    [JsonIgnore]
    public string Glyph => Kind.StartsWith("Restes", StringComparison.Ordinal) ? "" : "";
}

/// <summary>Historique des nettoyages, enregistré dans %LocalAppData%\Uninstallix\historique.json.</summary>
public static class CleanupHistory
{
    private const int MaxEntries = 500;
    private static readonly string FilePath = Path.Combine(Journal.Folder, "historique.json");
    private static readonly object Lock = new();

    public static List<HistoryEntry> Load()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex)
            {
                Journal.Write("Historique illisible : " + ex.Message);
            }
            return new();
        }
    }

    public static void Add(string kind, IEnumerable<string> items, long freedBytes, int errors)
    {
        lock (Lock)
        {
            var entries = Load();
            entries.Insert(0, new HistoryEntry
            {
                Date = DateTime.Now,
                Kind = kind,
                Items = items.ToList(),
                FreedBytes = Math.Max(0, freedBytes),
                Errors = errors,
            });
            if (entries.Count > MaxEntries)
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            Save(entries);
        }
    }

    public static void Clear()
    {
        lock (Lock)
            Save(new List<HistoryEntry>());
    }

    private static void Save(List<HistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Journal.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Journal.Write("Impossible d'enregistrer l'historique : " + ex.Message);
        }
    }
}
