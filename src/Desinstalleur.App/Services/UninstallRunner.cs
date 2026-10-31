using System.ComponentModel;
using System.Diagnostics;
using Desinstalleur.App.Models;
using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

public enum UninstallOutcome
{
    Removed,
    StillInstalled,
    /// <summary>Le désinstalleur du logiciel n'existe plus (installation abîmée).</summary>
    MissingUninstaller,
    /// <summary>Le désinstalleur s'est terminé avec un code d'erreur et le logiciel est toujours là.</summary>
    UninstallerError,
    /// <summary>Le désinstalleur demande un redémarrage pour finir (codes 3010 / 1641).</summary>
    RebootRequired,
    Failed,
}

/// <param name="ExitCode">Code de sortie du désinstalleur, s'il a pu être lu.</param>
public sealed record UninstallResult(UninstallOutcome Outcome, string Message, int? ExitCode = null)
{
    /// <summary>
    /// Vrai si le désinstalleur officiel est absent ou en échec : une désinstallation forcée peut alors être proposée.
    /// </summary>
    public bool OfficialUninstallerBroken =>
        Outcome is UninstallOutcome.MissingUninstaller or UninstallOutcome.UninstallerError or UninstallOutcome.Failed;
}

/// <summary>Lance le désinstalleur officiel d'un logiciel et attend qu'il ait fini.</summary>
public static class UninstallRunner
{
    /// <summary>
    /// Noms des copies temporaires que lancent les désinstalleurs NSIS, Inno Setup, InstallShield…
    /// Le processus de départ se termine tout de suite et laisse travailler cette copie.
    /// </summary>
    private static readonly string[] HelperProcessPrefixes =
    {
        "au_", "un_a", "un_b", "unins", "_iu14d2n", "uninst", "uninstall",
    };

    public static async Task<UninstallResult> RunAsync(InstalledProgram program, CancellationToken cancel)
    {
        // Réglage « Désinstallation silencieuse » : on prend la commande sans fenêtre si le logiciel en fournit une.
        var useQuiet = AppSettings.Current.PreferQuietUninstall && !string.IsNullOrWhiteSpace(program.QuietUninstallString);
        var command = UninstallCommand.Parse(useQuiet ? program.QuietUninstallString! : program.UninstallString);
        if (command is null)
            return new(UninstallOutcome.Failed, L.T("Commande de désinstallation introuvable."));

        if (Path.IsPathRooted(command.FileName) && !File.Exists(command.FileName))
        {
            Journal.Write($"Désinstalleur introuvable pour « {program.DisplayName} » : {command.FileName}");
            return new(UninstallOutcome.MissingUninstaller, L.F("Son désinstalleur est introuvable : {0}", command.FileName));
        }

        Journal.Write($"Désinstallation de « {program.DisplayName} » : {command.FileName} {command.Arguments}");

        var helpersBefore = SnapshotHelperProcesses();
        int? exitCode = null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = true,
                WorkingDirectory = SafeWorkingDirectory(command.FileName),
            });

            if (process is not null)
            {
                await process.WaitForExitAsync(cancel);
                exitCode = ReadExitCode(process);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(UninstallOutcome.StillInstalled, L.T("Autorisation refusée (contrôle de compte d'utilisateur)."));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return new(UninstallOutcome.MissingUninstaller, L.F("Son désinstalleur est introuvable : {0}", command.FileName));
        }
        catch (Win32Exception ex)
        {
            return new(UninstallOutcome.Failed, L.F("Impossible de lancer le désinstalleur : {0}", ex.Message));
        }

        // Attendre les éventuels processus « relais » lancés par le désinstalleur.
        await Task.Delay(1500, cancel);
        var deadline = DateTime.UtcNow.AddHours(1);
        while (DateTime.UtcNow < deadline && HasNewHelperProcess(helpersBefore))
            await Task.Delay(1000, cancel);

        // Certains désinstalleurs suppriment leur clé avec un léger retard.
        for (var i = 0; i < 5 && ProgramInventory.StillExists(program); i++)
            await Task.Delay(1000, cancel);

        if (ProgramInventory.StillExists(program))
        {
            Journal.Write($"« {program.DisplayName} » est toujours installé après le désinstalleur (code de sortie : {exitCode?.ToString() ?? "inconnu"}).");
            if (exitCode is 3010 or 1641)
                return new(UninstallOutcome.RebootRequired,
                    L.T("Redémarrez l'ordinateur pour terminer la désinstallation."), exitCode);
            if (exitCode is { } code && code != 0 && !CancelExitCodes.Contains(code))
                return new(UninstallOutcome.UninstallerError,
                    L.F("Le désinstalleur s'est terminé avec une erreur (code {0}).", code), exitCode);
            return new(UninstallOutcome.StillInstalled,
                L.T("Le logiciel est toujours installé : la désinstallation a peut-être été annulée."), exitCode);
        }

        Journal.Write($"« {program.DisplayName} » désinstallé.");
        return new(UninstallOutcome.Removed, L.T("Désinstallé avec succès."), exitCode);
    }

    /// <summary>Codes qui signifient « annulé par l'utilisateur » (Windows Installer 1602, ERROR_CANCELLED 1223).</summary>
    private static readonly HashSet<int> CancelExitCodes = new() { 1602, 1223 };

    private static int? ReadExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string SafeWorkingDirectory(string fileName)
    {
        try
        {
            var dir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }
        catch (ArgumentException)
        {
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.System);
    }

    private static HashSet<int> SnapshotHelperProcesses() =>
        Process.GetProcesses().Where(IsHelper).Select(p => p.Id).ToHashSet();

    private static bool HasNewHelperProcess(HashSet<int> before) =>
        Process.GetProcesses().Any(p => IsHelper(p) && !before.Contains(p.Id));

    private static bool IsHelper(Process process)
    {
        try
        {
            var name = process.ProcessName.ToLowerInvariant();
            return HelperProcessPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
