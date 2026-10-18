using System.Diagnostics;

namespace Desinstalleur.App.Services;

/// <summary>
/// Mode portable : Uninstallix tourne sans installation, depuis n'importe quel dossier (clé USB…).
/// Actif quand l'exe s'appelle « …Portable… », quand il est lancé avec « --portable »,
/// ou quand un fichier « portable.txt » se trouve à côté de lui.
/// Toutes les données (réglages, journal, historique, sauvegardes) vont alors dans « Uninstallix-Data » à côté de l'exe.
/// </summary>
public static class PortableMode
{
    public const string Argument = "--portable";
    public const string MarkerFile = "portable.txt";
    public const string DataFolderName = "Uninstallix-Data";

    private static string ExePath => Environment.ProcessPath ?? string.Empty;
    private static string ExeFolder => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;

    /// <summary>Lancé avec « --portable » (à transmettre lors d'un redémarrage).</summary>
    public static bool FromArgument { get; } = Environment.GetCommandLineArgs().Skip(1)
        .Any(a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));

    public static bool IsActive { get; } = Detect();

    /// <summary>Pourquoi le dossier portable n'a pas pu servir (écrit dans le journal au démarrage), sinon null.</summary>
    public static string? FallbackReason { get; private set; }

    private static bool Detect()
    {
        if (FromArgument)
            return true;
        try
        {
            if (Path.GetFileNameWithoutExtension(ExePath).Contains("portable", StringComparison.OrdinalIgnoreCase))
                return true;
            return File.Exists(Path.Combine(ExeFolder, MarkerFile));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Dossier de données portable s'il est utilisable, sinon null (on reste alors dans %LocalAppData%).
    /// N'écrit jamais dans le journal : Journal.Folder dépend de ce résultat.
    /// </summary>
    public static string? ResolveDataFolder()
    {
        if (!IsActive)
            return null;
        var folder = Path.Combine(ExeFolder, DataFolderName);
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".test-{Environment.ProcessId}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return folder;
        }
        catch (Exception ex)
        {
            FallbackReason = $"{folder} : {ex.Message}";
            return null;
        }
    }

    /// <summary>Arguments à passer pour relancer l'appli dans le même mode.</summary>
    public static string RestartArguments => FromArgument ? Argument : string.Empty;

    /// <summary>Supprime l'ancien exe « .old » laissé par une mise à jour portable (il peut encore se fermer).</summary>
    public static void DeleteLeftoverOldExe()
    {
        var old = ExePath + ".old";
        if (string.IsNullOrEmpty(ExePath) || !File.Exists(old))
            return;
        Task.Run(async () =>
        {
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    File.Delete(old);
                    Journal.Write("Ancienne version portable supprimée : " + old);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(1000);
                }
            }
            Journal.Write("Ancienne version portable impossible à supprimer pour l'instant : " + old);
        });
    }

    /// <summary>
    /// Remplace l'exe en cours par la nouvelle version téléchargée puis la lance.
    /// Windows permet de renommer un exe ouvert : l'actuel devient « .old » et sera supprimé au prochain démarrage.
    /// L'appelant ferme ensuite l'appli.
    /// </summary>
    public static void ReplaceAndRestart(string downloadedExe)
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe))
            throw new InvalidOperationException(L.T("Chemin de l'exécutable introuvable."));
        var old = exe + ".old";
        if (File.Exists(old))
            File.Delete(old);

        File.Move(exe, old);
        try
        {
            File.Move(downloadedExe, exe);
        }
        catch
        {
            // Retour en arrière : on remet l'exe d'origine à sa place.
            try
            {
                File.Move(old, exe);
            }
            catch (Exception rollback)
            {
                Journal.Write("Restauration de l'exe portable impossible : " + rollback.Message);
            }
            throw;
        }
        Journal.Write($"Version portable remplacée : {exe}");
        Process.Start(new ProcessStartInfo(exe, RestartArguments) { UseShellExecute = true, WorkingDirectory = ExeFolder });
    }
}
