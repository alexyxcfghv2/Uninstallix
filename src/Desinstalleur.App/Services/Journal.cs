namespace Desinstalleur.App.Services;

/// <summary>
/// Journal texte de toutes les actions (désinstallations, suppressions, sauvegardes),
/// dans %LocalAppData%\Uninstallix.
/// </summary>
public static class Journal
{
    public static string Folder { get; } = Path.Combine(
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
