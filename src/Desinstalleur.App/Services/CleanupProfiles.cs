namespace Desinstalleur.App.Services;

/// <summary>Profils de nettoyage : une sélection de caches par profil, modifiable par l'utilisateur.</summary>
public static class CleanupProfiles
{
    public const string Custom = "Personnalisé";
    public const string Gaming = "Gaming";
    public const string Browsers = "Navigateurs";

    public static readonly string[] All = { Custom, Gaming, Browsers };

    public static Dictionary<string, List<string>> Defaults() => new()
    {
        [Custom] = new() { "Fichiers temporaires", "FiveM", "Discord", "Rapports d'erreurs" },
        [Gaming] = new() { "Fichiers temporaires", "FiveM", "Discord", "Steam", "Roblox", "Cache des shaders" },
        [Browsers] = new() { "Google Chrome", "Microsoft Edge" },
    };

    /// <summary>Sélection enregistrée d'un profil (valeurs par défaut si absente).</summary>
    public static List<string> Selection(string profile)
    {
        var settings = AppSettings.Current;
        if (!settings.Profiles.TryGetValue(profile, out var names))
        {
            names = Defaults().TryGetValue(profile, out var defaults) ? defaults : new List<string>();
            settings.Profiles[profile] = names;
        }
        return names;
    }

    public static void SaveSelection(string profile, IEnumerable<string> names)
    {
        AppSettings.Current.Profiles[profile] = names.ToList();
        AppSettings.Current.ActiveProfile = profile;
        AppSettings.Current.Save();
    }
}
