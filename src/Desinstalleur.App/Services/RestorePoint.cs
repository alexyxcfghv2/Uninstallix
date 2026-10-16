using System.Diagnostics;

namespace Desinstalleur.App.Services;

/// <summary>Crée un point de restauration Windows (via PowerShell « Checkpoint-Computer »).</summary>
public static class RestorePoint
{
    /// <summary>Renvoie null si tout s'est bien passé, sinon un message d'erreur lisible.</summary>
    public static string? Create(string description)
    {
        try
        {
            var safeDescription = description.Replace("'", "''");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                            $"\"Checkpoint-Computer -Description '{safeDescription}' -RestorePointType 'APPLICATION_UNINSTALL' -ErrorAction Stop\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
                return L.T("PowerShell n'a pas pu être lancé.");

            var error = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(180_000))
                return L.T("La création du point de restauration a pris trop de temps.");

            if (process.ExitCode != 0)
            {
                var message = error.Result.Trim();
                Journal.Write("Point de restauration impossible : " + message);
                return L.T("La protection du système est peut-être désactivée sur ce PC.");
            }

            Journal.Write("Point de restauration demandé : " + description);
            return null;
        }
        catch (Exception ex)
        {
            Journal.Write("Point de restauration impossible : " + ex.Message);
            return ex.Message;
        }
    }
}
