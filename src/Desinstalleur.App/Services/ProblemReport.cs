using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Desinstalleur.App.Services;

/// <summary>Prépare le texte d'un rapport de problème, avec masquage des informations personnelles.</summary>
public static class ProblemReport
{
    public static string Build(string description, bool includeJournal, bool includeSettings, bool anonymize)
    {
        var memory = MemoryOptimizer.GetStatus();
        var text = new StringBuilder();
        text.AppendLine("=== Rapport de problème Uninstallix ===");
        text.AppendLine($"Date : {DateTime.Now:dd/MM/yyyy HH:mm}");
        text.AppendLine($"Version d'Uninstallix : {SelfInstaller.Version}");
        text.AppendLine($"Windows : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine($".NET : {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"Mémoire : {Desinstalleur.Core.ValueParsers.FormatSize((long)memory.TotalBytes)}  ·  utilisée à {memory.UsedPercent:0} %");
        text.AppendLine($"Langue : {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        text.AppendLine();
        text.AppendLine("--- Description ---");
        text.AppendLine(string.IsNullOrWhiteSpace(description) ? "(non renseignée)" : description.Trim());

        if (includeSettings)
        {
            text.AppendLine();
            text.AppendLine("--- Réglages ---");
            var s = AppSettings.Current;
            text.AppendLine($"Point de restauration : {OuiNon(s.CreateRestorePoint)}  ·  Silencieuse : {OuiNon(s.PreferQuietUninstall)}  ·  " +
                            $"Cochage auto : {OuiNon(s.AutoSelectSafeLeftovers)}  ·  Corbeille : {OuiNon(s.UseRecycleBin)}");
            text.AppendLine($"Composants système : {OuiNon(s.ShowSystemComponents)}  ·  Garder temp récents : {OuiNon(s.KeepRecentTempFiles)}  ·  " +
                            $"Animations : {OuiNon(s.EnableAnimations)}  ·  Profil : {s.ActiveProfile}");
            text.AppendLine($"Favoris protégés : {s.ProtectedFolders.Count} dossier(s), {s.ProtectedPrograms.Count} logiciel(s)");
        }

        if (includeJournal)
        {
            text.AppendLine();
            text.AppendLine("--- Journal (200 dernières lignes) ---");
            var journal = Path.Combine(Journal.Folder, "journal.txt");
            try
            {
                if (File.Exists(journal))
                {
                    using var stream = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    var lines = reader.ReadToEnd().Split('\n');
                    foreach (var line in lines.Skip(Math.Max(0, lines.Length - 200)))
                        text.AppendLine(line.TrimEnd('\r'));
                }
                else
                {
                    text.AppendLine("(journal vide)");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                text.AppendLine("(journal illisible : " + ex.Message + ")");
            }
        }

        var result = text.ToString();
        return anonymize ? Anonymize(result) : result;
    }

    /// <summary>Remplace le nom d'utilisateur, le nom du PC, les dossiers personnels et les adresses e-mail.</summary>
    public static string Anonymize(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            text = Regex.Replace(text, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
        // Autres profils éventuels (C:\Users\quelqu'un\...)
        text = Regex.Replace(text, @"(?i)([A-Z]:\\(?:Users|Utilisateurs)\\)(?!Public\\|Default\\)[^\\\r\n""]+", "$1<utilisateur>");
        if (Environment.UserName.Length > 2)
            text = Regex.Replace(text, $@"\b{Regex.Escape(Environment.UserName)}\b", "<utilisateur>", RegexOptions.IgnoreCase);
        if (Environment.MachineName.Length > 2)
            text = Regex.Replace(text, $@"\b{Regex.Escape(Environment.MachineName)}\b", "<pc>", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[\w.+-]+@[\w-]+\.[\w.-]+", "<e-mail>");
        return text;
    }

    private static string OuiNon(bool value) => value ? "oui" : "non";
}
