using System.Text.RegularExpressions;

namespace Desinstalleur.Core;

/// <summary>
/// Repère les composants essentiels (runtimes, pilotes, composants Windows) que
/// l'application verrouille par défaut pour éviter une désinstallation accidentelle.
/// </summary>
public static class ProtectionRules
{
    private sealed record Rule(Regex Pattern, string Reason);

    private static Rule R(string pattern, string reason) =>
        new(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant), reason);

    private static readonly Rule[] Rules =
    {
        R(@"^Microsoft Visual C\+\+ .*(Redistributable|Runtime)", "Bibliothèque utilisée par de nombreux logiciels"),
        R(@"^Microsoft (\.NET|ASP\.NET|Windows Desktop Runtime)", "Environnement .NET utilisé par d'autres logiciels"),
        R(@"^Microsoft Edge( WebView2)?( Runtime)?$|WebView2", "Composant de Windows utilisé par d'autres logiciels"),
        R(@"^Windows App ?Runtime|^Microsoft Windows App ?Runtime|^Microsoft\.WindowsAppRuntime", "Composant de Windows"),
        R(@"^Microsoft Update Health Tools|^Update for Windows|^Windows PC Health Check|^Microsoft GameInput", "Composant de mise à jour de Windows"),
        R(@"^Windows Driver Package|\bdriver\b|\bpilote\b|chipset|Intel\(R\) Management Engine", "Pilote matériel"),
        R(@"^Intel\(R\)|^Realtek|^NVIDIA (Graphics|PhysX|HD Audio|FrameView|USBC)|^AMD (Chipset|Software)", "Pilote ou logiciel matériel"),
        R(@"DirectX|^Microsoft XNA|^Vulkan Run ?Time", "Bibliothèque graphique utilisée par les jeux"),
        R(@"^Java( \d+)?( Update \d+)?( \(64-bit\))?$|^Java\(TM\)", "Environnement Java utilisé par d'autres logiciels"),
    };

    /// <summary>Renvoie la raison de la protection, ou null si le logiciel n'est pas protégé.</summary>
    public static string? GetProtectionReason(string? displayName, string? publisher, bool isSystemComponent)
    {
        if (isSystemComponent)
            return "Composant système de Windows";
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        foreach (var rule in Rules)
        {
            if (rule.Pattern.IsMatch(displayName))
                return rule.Reason;
        }

        return null;
    }
}
