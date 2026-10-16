using System.Text.RegularExpressions;

namespace Desinstalleur.Core;

/// <summary>
/// Commande prête à être lancée : un exécutable et ses arguments.
/// </summary>
public sealed record UninstallCommand(string FileName, string Arguments)
{
    private static readonly Regex MsiProductCode =
        new(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}", RegexOptions.Compiled);

    public bool IsMsi => FileName.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)
                         || FileName.Equals("msiexec", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Transforme la valeur « UninstallString » du registre en commande exécutable.
    /// Gère les chemins entre guillemets, les chemins avec espaces sans guillemets
    /// et les commandes MsiExec (on force /X pour désinstaller, jamais /I qui « modifie »).
    /// </summary>
    public static UninstallCommand? Parse(string? uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
            return null;

        var raw = Environment.ExpandEnvironmentVariables(uninstallString.Trim());

        if (raw.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var match = MsiProductCode.Match(raw);
            if (match.Success)
                return new UninstallCommand("msiexec.exe", $"/X{match.Value}");
        }

        string file;
        string args;

        if (raw.StartsWith('"'))
        {
            var closing = raw.IndexOf('"', 1);
            if (closing < 0)
            {
                file = raw.Trim('"');
                args = string.Empty;
            }
            else
            {
                file = raw[1..closing];
                args = raw[(closing + 1)..].Trim();
            }
        }
        else
        {
            // Chemin sans guillemets : « C:\Program Files\App\uninst.exe /S ».
            // On coupe juste après le premier « .exe » suivi d'un espace ou de la fin.
            var exeEnd = FindExeEnd(raw);
            if (exeEnd > 0)
            {
                file = raw[..exeEnd];
                args = raw[exeEnd..].Trim();
            }
            else
            {
                var space = raw.IndexOf(' ');
                file = space < 0 ? raw : raw[..space];
                args = space < 0 ? string.Empty : raw[(space + 1)..].Trim();
            }
        }

        return file.Length == 0 ? null : new UninstallCommand(file, args);
    }

    private static int FindExeEnd(string raw)
    {
        var search = 0;
        while (true)
        {
            var idx = raw.IndexOf(".exe", search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return -1;
            var end = idx + 4;
            if (end == raw.Length || raw[end] == ' ')
                return end;
            search = end;
        }
    }
}
