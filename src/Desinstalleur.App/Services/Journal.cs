namespace Desinstalleur.App.Services;

/// <summary>
/// Journal texte de toutes les actions (désinstallations, suppressions, sauvegardes),
/// dans %LocalAppData%\Uninstallix (ou « Uninstallix-Data » à côté de l'exe en mode portable).
/// </summary>
public static class Journal
{
    /// <summary>
    /// Dossier de toutes les données d'Uninstallix (réglages, journal, historique, sauvegardes, analyses).
    /// C'est le seul aiguillage du mode portable : toutes les fonctions passent par lui.
    /// </summary>
    public static string Folder { get; } = PortableMode.ResolveDataFolder() ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Uninstallix");

    public static string RegistryBackupFolder => Path.Combine(Folder, "Sauvegardes registre");

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, "journal.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Le journal ne doit jamais faire échouer une opération.
        }
    }
}
