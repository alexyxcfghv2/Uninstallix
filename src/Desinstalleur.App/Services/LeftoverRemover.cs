using System.Diagnostics;
using Desinstalleur.App.Models;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>
/// Supprime les restes que l'utilisateur a cochés ET confirmés.
/// Fichiers et dossiers partent à la Corbeille ; chaque clé de registre est
/// exportée dans un fichier .reg avant d'être supprimée.
/// </summary>
public sealed class LeftoverRemover
{
    private readonly SafetyGuard _guard;

    public LeftoverRemover(SafetyGuard guard) => _guard = guard;

    public void Remove(LeftoverItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case LeftoverKind.Folder:
                case LeftoverKind.File:
                    RemovePath(item);
                    break;
                case LeftoverKind.RegistryKey:
                    RemoveKey(item);
                    break;
            }
        }
        catch (Exception ex)
        {
            Fail(item, ex.Message);
        }
    }

    private void RemovePath(LeftoverItem item)
    {
        // Revérification juste avant la suppression : rien ne doit passer entre les mailles.
        var refusal = _guard.CheckPath(item.Path);
        if (refusal is not null)
        {
            Fail(item, refusal);
            return;
        }

        var exists = item.Kind == LeftoverKind.Folder ? Directory.Exists(item.Path) : File.Exists(item.Path);
        if (!exists)
        {
            Succeed(item, "Déjà absent");
            return;
        }

        if (File.GetAttributes(item.Path).HasFlag(FileAttributes.ReparsePoint))
        {
            Fail(item, "Lien symbolique ou jonction : ignoré par sécurité.");
            return;
        }

        if (!AppSettings.Current.UseRecycleBin)
        {
            DeletePermanently(item);
            return;
        }

        var code = NativeMethods.SendToRecycleBin(item.Path);
        var stillThere = item.Kind == LeftoverKind.Folder ? Directory.Exists(item.Path) : File.Exists(item.Path);
        if (code == 0 && !stillThere)
        {
            Journal.Write($"Envoyé à la Corbeille : {item.Path}");
            Succeed(item, "Envoyé à la Corbeille");
        }
        else
        {
            Fail(item, code == 0 ? "Certains fichiers sont utilisés ou protégés." : $"Erreur Windows {code}.");
        }
    }

    /// <summary>Suppression sans Corbeille (réglage désactivé). Les jonctions internes ne sont jamais suivies.</summary>
    private static void DeletePermanently(LeftoverItem item)
    {
        var failures = 0;
        if (item.Kind == LeftoverKind.File)
        {
            try
            {
                File.SetAttributes(item.Path, FileAttributes.Normal);
                File.Delete(item.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures++;
            }
        }
        else
        {
            failures = DeleteTree(new DirectoryInfo(item.Path));
        }

        if (failures == 0)
        {
            Journal.Write($"Supprimé définitivement : {item.Path}");
            Succeed(item, "Supprimé définitivement");
        }
        else
        {
            Fail(item, $"{failures} élément(s) utilisés ou protégés n'ont pas pu être supprimés.");
        }
    }

    private static int DeleteTree(DirectoryInfo dir)
    {
        var failures = 0;
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = dir.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 1;
        }

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // Supprime le lien lui-même, jamais ce vers quoi il pointe.
                    if (entry is DirectoryInfo link)
                        link.Delete(false);
                    else
                        entry.Delete();
                }
                else if (entry is DirectoryInfo sub)
                {
                    failures += DeleteTree(sub);
                }
                else
                {
                    entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures++;
            }
        }

        if (failures == 0)
        {
            try
            {
                dir.Delete(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures++;
            }
        }
        return failures;
    }

    private void RemoveKey(LeftoverItem item)
    {
        var refusal = SafetyGuard.CheckRegistryKey(item.Path);
        if (refusal is not null || string.IsNullOrEmpty(item.RegistrySubKey))
        {
            Fail(item, refusal ?? "Clé invalide.");
            return;
        }

        using var root = RegistryKey.OpenBaseKey(item.Hive, RegistryView.Registry64);
        using (var existing = root.OpenSubKey(item.RegistrySubKey))
        {
            if (existing is null)
            {
                Succeed(item, "Déjà absente");
                return;
            }
        }

        var backup = BackupKey(item.Path);
        if (backup is null)
        {
            Fail(item, "Sauvegarde impossible : clé conservée par sécurité.");
            return;
        }

        root.DeleteSubKeyTree(item.RegistrySubKey, throwOnMissingSubKey: false);
        Journal.Write($"Clé supprimée : {item.Path} (sauvegarde : {backup})");
        Succeed(item, "Supprimée (sauvegarde .reg créée)");
    }

    /// <summary>Exporte la clé avec reg.exe. Renvoie le chemin du fichier, ou null en cas d'échec.</summary>
    private static string? BackupKey(string fullKeyPath)
    {
        Directory.CreateDirectory(Journal.RegistryBackupFolder);
        var safeName = string.Concat(fullKeyPath.Split(Path.GetInvalidFileNameChars())).Replace('\\', '_');
        if (safeName.Length > 120)
            safeName = safeName[^120..];
        var file = Path.Combine(Journal.RegistryBackupFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}_{safeName}.reg");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe"),
            ArgumentList = { "export", fullKeyPath, file, "/y", "/reg:64" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (process is null)
            return null;
        process.WaitForExit(30_000);
        return process.HasExited && process.ExitCode == 0 && File.Exists(file) ? file : null;
    }

    private static void Succeed(LeftoverItem item, string text)
    {
        item.Succeeded = true;
        item.ResultText = text;
    }

    private static void Fail(LeftoverItem item, string text)
    {
        Journal.Write($"Échec pour {item.Path} : {text}");
        item.Succeeded = false;
        item.ResultText = text;
    }
}
