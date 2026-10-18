using System.Diagnostics;
using Desinstalleur.App.Models;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>
/// Liste les applications lancées avec Windows et les active / désactive exactement comme le Gestionnaire des tâches :
/// l'état est stocké dans les clés « StartupApproved » ; les entrées Run et les raccourcis ne sont jamais supprimés.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupEntry> Load()
    {
        var entries = new List<StartupEntry>();
        ReadRun(entries, RegistryHive.CurrentUser, RunKey, "Run");
        ReadRun(entries, RegistryHive.LocalMachine, RunKey, "Run");
        ReadRun(entries, RegistryHive.LocalMachine, Run32Key, "Run32");
        ReadFolder(entries, RegistryHive.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        ReadFolder(entries, RegistryHive.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
        return entries.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Active ou désactive une entrée (valeur binaire de 12 octets, comme le Gestionnaire des tâches).</summary>
    public static void SetEnabled(StartupEntry entry, bool enabled)
    {
        var data = new byte[12];
        if (enabled)
        {
            data[0] = 0x02;
        }
        else
        {
            data[0] = 0x03;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }

        using var root = RegistryKey.OpenBaseKey(entry.Hive, RegistryView.Registry64);
        using var key = root.CreateSubKey(ApprovedRoot + entry.ApprovedSubKey, writable: true)
                        ?? throw new UnauthorizedAccessException();
        key.SetValue(entry.ValueName, data, RegistryValueKind.Binary);
        entry.IsEnabled = enabled;

        Journal.Write($"Démarrage : « {entry.DisplayName} » ({entry.ValueName}, {entry.Hive} {entry.ApprovedSubKey}) "
                      + (enabled ? "activé" : "désactivé"));
    }

    // ---- Lecture ----

    private static void ReadRun(List<StartupEntry> entries, RegistryHive hive, string path, string approvedSubKey)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            if (key is null)
                return;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                try
                {
                    if (key.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command))
                        continue;
                    var exe = ExtractExe(command);
                    entries.Add(Build(StartupSourceKind.Registry, hive, (hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + path,
                        name, approvedSubKey, command, exe, null, exe is null || !File.Exists(exe)));
                }
                catch (Exception ex)
                {
                    Journal.Write($"Démarrage : valeur « {name} » illisible dans {path} : {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Journal.Write($"Démarrage : lecture de {hive}\\{path} impossible : {ex.Message}");
        }
    }

    private static void ReadFolder(List<StartupEntry> entries, RegistryHive hive, string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    string? exe = file;
                    var command = file;
                    if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var (target, args) = ResolveShortcut(file);
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            exe = target;
                            command = string.IsNullOrWhiteSpace(args) ? $"\"{target}\"" : $"\"{target}\" {args}";
                        }
                    }
                    var missing = exe is null || !File.Exists(exe);
                    entries.Add(Build(StartupSourceKind.Folder, hive, folder, fileName, "StartupFolder", command, exe, file, missing));
                }
                catch (Exception ex)
                {
                    Journal.Write($"Démarrage : fichier « {file} » illisible : {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Journal.Write($"Démarrage : lecture du dossier {folder} impossible : {ex.Message}");
        }
    }

    private static StartupEntry Build(StartupSourceKind kind, RegistryHive hive, string location, string valueName,
        string approvedSubKey, string command, string? exe, string? filePath, bool missing)
    {
        string? description = null, product = null, company = null;
        if (!missing && exe is not null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                description = info.FileDescription?.Trim();
                product = info.ProductName?.Trim();
                company = info.CompanyName?.Trim();
            }
            catch
            {
                // Pas d'informations de version : on garde le nom de la valeur.
            }
        }

        var fallback = kind == StartupSourceKind.Folder ? Path.GetFileNameWithoutExtension(valueName) : valueName;
        var name = !string.IsNullOrWhiteSpace(description) ? description!
                 : !string.IsNullOrWhiteSpace(product) ? product!
                 : fallback;

        return new StartupEntry
        {
            Kind = kind,
            Hive = hive,
            Location = location,
            ValueName = valueName,
            ApprovedSubKey = approvedSubKey,
            Command = command,
            ExePath = exe,
            FilePath = filePath,
            FileMissing = missing,
            DisplayName = name,
            Publisher = string.IsNullOrWhiteSpace(company) ? null : company,
            IsEnabled = ReadEnabled(hive, approvedSubKey, valueName),
        };
    }

    /// <summary>Valeur absente ou premier octet pair = activé ; impair = désactivé.</summary>
    private static bool ReadEnabled(RegistryHive hive, string approvedSubKey, string valueName)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(ApprovedRoot + approvedSubKey);
            if (key?.GetValue(valueName) is byte[] { Length: > 0 } data)
                return (data[0] & 1) == 0;
        }
        catch
        {
            // Illisible : on considère l'entrée active, comme Windows.
        }
        return true;
    }

    private static (string? Target, string? Arguments) ResolveShortcut(string lnk)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return (null, null);
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic link = shell.CreateShortcut(lnk);
                string? target = link.TargetPath;
                string? args = link.Arguments;
                return (string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target), args);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Retrouve l'exécutable au début d'une ligne de commande (avec ou sans guillemets).</summary>
    public static string? ExtractExe(string command)
    {
        var value = Environment.ExpandEnvironmentVariables(command.Trim());
        if (value.Length == 0)
            return null;

        if (value[0] == '"')
        {
            var end = value.IndexOf('"', 1);
            var quoted = end > 1 ? value[1..end] : value[1..];
            return WithExe(quoted);
        }

        // Sans guillemets : le chemin peut contenir des espaces (« C:\Program Files\App\app.exe /min »).
        var lower = value.ToLowerInvariant();
        var exeIndex = lower.IndexOf(".exe", StringComparison.Ordinal);
        if (exeIndex > 0)
            return value[..(exeIndex + 4)];

        var space = value.IndexOf(' ');
        var first = space > 0 ? value[..space] : value;
        for (var i = space; i > 0; i = value.IndexOf(' ', i + 1))
        {
            var candidate = value[..i];
            if (File.Exists(candidate))
                return candidate;
        }
        return WithExe(first);
    }

    /// <summary>Complète « rundll32 » ou « explorer » en chemin réel quand le fichier est dans System32 ou Windows.</summary>
    private static string WithExe(string path)
    {
        if (File.Exists(path) || Path.IsPathRooted(path))
            return path;
        var name = Path.HasExtension(path) ? path : path + ".exe";
        foreach (var folder in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return path;
    }
}
