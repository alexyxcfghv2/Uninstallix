namespace Desinstalleur.App.Services;

/// <summary>
/// Favoris protégés : dossiers et logiciels que l'utilisateur ne veut jamais voir proposés au nettoyage.
/// </summary>
public static class Favorites
{
    public static IReadOnlyList<string> Folders => AppSettings.Current.ProtectedFolders;
    public static IReadOnlyList<string> Programs => AppSettings.Current.ProtectedPrograms;

    /// <summary>Vrai si le chemin est un dossier protégé ou se trouve dedans.</summary>
    public static bool IsProtectedPath(string path)
    {
        if (AppSettings.Current.ProtectedFolders.Count == 0)
            return false;
        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd('\\');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        foreach (var folder in AppSettings.Current.ProtectedFolders)
        {
            var root = folder.TrimEnd('\\');
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Vrai si le chemin contient un dossier protégé (le supprimer emporterait le favori).</summary>
    public static bool ContainsProtectedPath(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd('\\');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        return AppSettings.Current.ProtectedFolders.Any(f =>
            f.TrimEnd('\\').StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsProtectedProgram(string displayName) =>
        AppSettings.Current.ProtectedPrograms.Contains(displayName, StringComparer.OrdinalIgnoreCase);

    public static void AddFolder(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd('\\');
        if (!AppSettings.Current.ProtectedFolders.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            AppSettings.Current.ProtectedFolders.Add(full);
            AppSettings.Current.Save();
        }
    }

    public static void RemoveFolder(string folder)
    {
        AppSettings.Current.ProtectedFolders.RemoveAll(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase));
        AppSettings.Current.Save();
    }

    public static void AddProgram(string displayName)
    {
        if (!IsProtectedProgram(displayName))
        {
            AppSettings.Current.ProtectedPrograms.Add(displayName);
            AppSettings.Current.Save();
        }
    }

    public static void RemoveProgram(string displayName)
    {
        AppSettings.Current.ProtectedPrograms.RemoveAll(p => p.Equals(displayName, StringComparison.OrdinalIgnoreCase));
        AppSettings.Current.Save();
    }
}
