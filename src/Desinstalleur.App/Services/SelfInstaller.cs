using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>
/// Installation et désinstallation d'Uninstallix lui-même.
/// Le même exécutable sert d'installateur : renommé « Uninstallix-Setup.exe », il ouvre l'assistant ;
/// lancé avec « --desinstaller », il se retire du PC.
/// </summary>
public static class SelfInstaller
{
    public const string UninstallArgument = "--desinstaller";
    private const string ExeName = "Uninstallix.exe";
    private const string ShortcutName = "Uninstallix.lnk";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uninstallix";

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Uninstallix");

    /// <summary>Vrai quand l'exécutable s'appelle « …Setup… » : on affiche l'assistant au lieu de l'appli.</summary>
    public static bool IsSetupMode(string[] args)
    {
        if (args.Any(a => a.Equals("--installer", StringComparison.OrdinalIgnoreCase)))
            return true;
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        return name.Contains("setup", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUninstallMode(string[] args) =>
        args.Any(a => a.Equals(UninstallArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>Dossier d'une installation existante (pour proposer la mise à jour), ou null.</summary>
    public static string? ExistingInstallFolder()
    {
        using var key = OpenUninstallKey(writable: false);
        var folder = key?.GetValue("InstallLocation") as string;
        return !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, ExeName)) ? folder : null;
    }

    /// <summary>Copie l'appli, crée les raccourcis et l'entrée « Applications installées ». Renvoie le chemin de l'exe installé.</summary>
    public static string Install(string folder, bool desktopShortcut, bool startMenuShortcut, Action<string> progress)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l'installateur introuvable.");
        folder = Path.GetFullPath(folder);
        var target = Path.Combine(folder, ExeName);

        progress("Fermeture d'Uninstallix s'il est ouvert…");
        CloseOtherInstances();

        progress("Copie des fichiers…");
        Directory.CreateDirectory(folder);
        CopyWithRetry(source, target);

        progress("Création des raccourcis…");
        var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
        var startLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutName);
        if (desktopShortcut)
            CreateShortcut(desktopLink, target, folder);
        else
            DeleteFile(desktopLink);
        if (startMenuShortcut)
            CreateShortcut(startLink, target, folder);
        else
            DeleteFile(startLink);

        progress("Inscription dans Windows…");
        using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey, writable: true))
        {
            key.SetValue("DisplayName", "Uninstallix");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "alexy");
            key.SetValue("DisplayIcon", target + ",0");
            key.SetValue("InstallLocation", folder);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("UninstallString", $"\"{target}\" {UninstallArgument}");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024), RegistryValueKind.DWord);
        }

        RemoveOldScriptInstall(folder);
        Journal.Write($"Uninstallix {Version} installé dans {folder}");
        return target;
    }

    /// <summary>Retire raccourcis et entrée Windows, puis supprime le dossier une fois l'appli fermée.</summary>
    public static void Uninstall(bool removeUserData)
    {
        var folder = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;

        DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName));
        DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutName));
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        Journal.Write("Uninstallix désinstallé.");

        var targets = new List<string>();
        // On ne supprime le dossier que s'il ne contient que notre exe : jamais un dossier partagé.
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder)
                .All(e => Path.GetFileName(e).Equals(ExeName, StringComparison.OrdinalIgnoreCase)))
            targets.Add(folder);
        else if (Environment.ProcessPath is { } exe)
            targets.Add(exe);
        if (removeUserData && Directory.Exists(Journal.Folder))
            targets.Add(Journal.Folder);

        // L'exe ne peut pas se supprimer lui-même pendant qu'il tourne : cmd attend sa fermeture.
        var commands = targets.Select(t => Directory.Exists(t) ? $"rmdir /s /q \"{t}\"" : $"del /f /q \"{t}\"");
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Arguments = "/c timeout /t 3 /nobreak >nul & " + string.Join(" & ", commands),
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    private static RegistryKey? OpenUninstallKey(bool writable) =>
        Registry.LocalMachine.OpenSubKey(UninstallKey, writable);

    private static void CloseOtherInstances()
    {
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("Uninstallix"))
        {
            try
            {
                if (process.Id == self)
                    continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                    process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void CopyWithRetry(string source, string target)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            return;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(1000);
            }
        }
    }

    private static void CreateShortcut(string linkPath, string target, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponible.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = target;
            link.WorkingDirectory = workingDirectory;
            link.IconLocation = target + ",0";
            link.Description = "Désinstalleur et nettoyeur pour Windows";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Retire l'ancienne installation par script (v0.3 « Installer.bat ») pour éviter les doublons.</summary>
    private static void RemoveOldScriptInstall(string newFolder)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName));
            var oldFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Uninstallix");
            if (File.Exists(Path.Combine(oldFolder, ExeName)) &&
                !string.Equals(Path.GetFullPath(oldFolder).TrimEnd('\\'), newFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(oldFolder, recursive: true);
        }
        catch (Exception ex)
        {
            Journal.Write("Nettoyage de l'ancienne installation incomplet : " + ex.Message);
        }
    }
}
