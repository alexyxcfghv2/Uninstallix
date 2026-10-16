using System.Text.Json;

namespace Desinstalleur.App.Services;

/// <summary>Réglages de l'utilisateur, enregistrés dans %LocalAppData%\Uninstallix\reglages.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(Journal.Folder, "reglages.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Current { get; } = Load();

    /// <summary>Point de restauration Windows avant chaque série de désinstallations.</summary>
    public bool CreateRestorePoint { get; set; } = true;

    /// <summary>Utilise « QuietUninstallString » quand le logiciel en propose une.</summary>
    public bool PreferQuietUninstall { get; set; }

    /// <summary>Coche d'office les restes classés « Sûr ».</summary>
    public bool AutoSelectSafeLeftovers { get; set; } = true;

    /// <summary>Restes envoyés à la Corbeille (sinon supprimés définitivement).</summary>
    public bool UseRecycleBin { get; set; } = true;

    /// <summary>Affiche les composants système dès l'ouverture.</summary>
    public bool ShowSystemComponents { get; set; }

    /// <summary>Le nettoyage ne touche pas aux fichiers temporaires de moins de 24 h.</summary>
    public bool KeepRecentTempFiles { get; set; } = true;

    /// <summary>Langue de l'interface : « auto » (celle de Windows), « fr » ou « en ».</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Petites animations de l'interface (apparitions, compteurs).</summary>
    public bool EnableAnimations { get; set; } = true;

    /// <summary>Cherche une nouvelle version sur GitHub au démarrage.</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>Dossiers jamais proposés au nettoyage (caches et restes).</summary>
    public List<string> ProtectedFolders { get; set; } = new();

    /// <summary>Logiciels marqués « favoris protégés » (nom affiché).</summary>
    public List<string> ProtectedPrograms { get; set; } = new();

    /// <summary>Profil de nettoyage actif et sélection de chaque profil (noms des caches cochés).</summary>
    public string ActiveProfile { get; set; } = CleanupProfiles.Custom;
    public Dictionary<string, List<string>> Profiles { get; set; } = CleanupProfiles.Defaults();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Journal.Write("Réglages illisibles, valeurs par défaut utilisées : " + ex.Message);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Journal.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Journal.Write("Impossible d'enregistrer les réglages : " + ex.Message);
        }
    }
}
