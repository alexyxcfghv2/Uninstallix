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
        text.AppendLine(L.T("=== Rapport de problème Uninstallix ==="));
        text.AppendLine(L.F("Date : {0:dd/MM/yyyy HH:mm}", DateTime.Now));
        text.AppendLine(L.F("Version d'Uninstallix : {0}", SelfInstaller.Version));
        text.AppendLine(L.F("Windows : {0} ({1})", RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture));
        text.AppendLine(L.F(".NET : {0}", RuntimeInformation.FrameworkDescription));
        text.AppendLine(L.F("Mémoire : {0}  ·  utilisée à {1:0} %", Desinstalleur.Core.ValueParsers.FormatSize((long)memory.TotalBytes), memory.UsedPercent));
        text.AppendLine(L.F("Langue : {0}", System.Globalization.CultureInfo.CurrentUICulture.Name));
        text.AppendLine();
        text.AppendLine(L.T("--- Description ---"));
        text.AppendLine(string.IsNullOrWhiteSpace(description) ? L.T("(non renseignée)") : description.Trim());

        if (includeSettings)
        {
            text.AppendLine();
            text.AppendLine(L.T("--- Réglages ---"));
            var s = AppSettings.Current;
            text.AppendLine(L.F("Point de restauration : {0}  ·  Silencieuse : {1}  ·  Cochage auto : {2}  ·  Corbeille : {3}",
                                OuiNon(s.CreateRestorePoint), OuiNon(s.PreferQuietUninstall), OuiNon(s.AutoSelectSafeLeftovers), OuiNon(s.UseRecycleBin)));
            text.AppendLine(L.F("Composants système : {0}  ·  Garder temp récents : {1}  ·  Animations : {2}  ·  Profil : {3}",
                                OuiNon(s.ShowSystemComponents), OuiNon(s.KeepRecentTempFiles), OuiNon(s.EnableAnimations), s.ActiveProfile));
            text.AppendLine(L.F("Favoris protégés : {0} dossier(s), {1} logiciel(s)", s.ProtectedFolders.Count, s.ProtectedPrograms.Count));
        }

        if (includeJournal)
        {
            text.AppendLine();
            text.AppendLine(L.T("--- Journal (200 dernières lignes) ---"));
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
                    text.AppendLine(L.T("(journal vide)"));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                text.AppendLine(L.F("(journal illisible : {0})", ex.Message));
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
        text = Regex.Replace(text, @"(?i)([A-Z]:\\(?:Users|Utilisateurs)\\)(?!Public\\|Default\\)[^\\\r\n""]+", "$1" + L.T("<utilisateur>"));
        if (Environment.UserName.Length > 2)
            text = Regex.Replace(text, $@"\b{Regex.Escape(Environment.UserName)}\b", L.T("<utilisateur>"), RegexOptions.IgnoreCase);
        if (Environment.MachineName.Length > 2)
            text = Regex.Replace(text, $@"\b{Regex.Escape(Environment.MachineName)}\b", "<pc>", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[\w.+-]+@[\w-]+\.[\w.-]+", "<e-mail>");
        return text;
    }

    private static string OuiNon(bool value) => value ? L.T("oui") : L.T("non");
}
