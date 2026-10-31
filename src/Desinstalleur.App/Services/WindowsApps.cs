using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Desinstalleur.App.Models;

namespace Desinstalleur.App.Services;

/// <summary>
/// Applications Windows préinstallées (paquets Appx / MSIX) : liste et suppression pour l'utilisateur actuel,
/// via Windows PowerShell (Get-AppxPackage / Remove-AppxPackage). Jamais -AllUsers, jamais de déprovisionnement :
/// une application supprimée se réinstalle depuis le Microsoft Store.
/// </summary>
public static class WindowsApps
{
    private sealed record Known(string Name, WindowsAppAdvice Advice, string? Note = null, bool Locked = false);

    private const string NoteXbox = "Nécessaire pour le Game Pass et certains jeux du Store.";
    private const string NoteCodec = "Permet à Windows de lire certains formats d'images ou de vidéos.";
    private const string NoteStore = "Permet d'installer et de mettre à jour les applications : sans elle, impossible de les réinstaller.";
    private const string NoteThirdParty = "Lien d'installation ajouté par Windows ou par le fabricant du PC.";
    private const string NoteUnknown = "Application absente de la liste intégrée : vérifiez ce que c'est avant de la supprimer.";
    private const string NoteOtherPublisher = "Application d'un autre éditeur, installée depuis le Store ou par le fabricant du PC.";

    // Noms exacts des paquets (sans tenir compte de la casse).
    private static readonly Dictionary<string, Known> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- Sans risque ----
        ["Microsoft.BingNews"] = new("Actualités (Microsoft News)", WindowsAppAdvice.Safe),
        ["Microsoft.BingWeather"] = new("Météo (MSN)", WindowsAppAdvice.Safe),
        ["Microsoft.BingSearch"] = new("Recherche Bing", WindowsAppAdvice.Safe),
        ["Microsoft.BingFinance"] = new("Finance (MSN)", WindowsAppAdvice.Safe),
        ["Microsoft.BingSports"] = new("Sports (MSN)", WindowsAppAdvice.Safe),
        ["Microsoft.GetHelp"] = new("Obtenir de l'aide", WindowsAppAdvice.Safe),
        ["Microsoft.Getstarted"] = new("Astuces", WindowsAppAdvice.Safe),
        ["Microsoft.MicrosoftSolitaireCollection"] = new("Microsoft Solitaire Collection", WindowsAppAdvice.Safe),
        ["Microsoft.People"] = new("Contacts", WindowsAppAdvice.Safe),
        ["Microsoft.WindowsFeedbackHub"] = new("Hub de commentaires", WindowsAppAdvice.Safe),
        ["Microsoft.ZuneMusic"] = new("Lecteur multimédia (Groove Musique)", WindowsAppAdvice.Safe, "VLC ou un autre lecteur fait la même chose."),
        ["Microsoft.ZuneVideo"] = new("Films et TV", WindowsAppAdvice.Safe),
        ["Microsoft.YourPhone"] = new("Mobile connecté", WindowsAppAdvice.Safe, "Utile seulement si vous reliez votre téléphone au PC."),
        ["Clipchamp.Clipchamp"] = new("Clipchamp", WindowsAppAdvice.Safe),
        ["Microsoft.Todos"] = new("Microsoft To Do", WindowsAppAdvice.Safe),
        ["Microsoft.MicrosoftOfficeHub"] = new("Microsoft 365 (Office)", WindowsAppAdvice.Safe, "Simple portail vers Office : vos logiciels Office installés ne sont pas touchés."),
        ["Microsoft.SkypeApp"] = new("Skype", WindowsAppAdvice.Safe),
        ["Microsoft.WindowsMaps"] = new("Cartes", WindowsAppAdvice.Safe),
        ["Microsoft.MixedReality.Portal"] = new("Portail de réalité mixte", WindowsAppAdvice.Safe),
        ["Microsoft.Microsoft3DViewer"] = new("Visionneuse 3D", WindowsAppAdvice.Safe),
        ["Microsoft.Print3D"] = new("Print 3D", WindowsAppAdvice.Safe),
        ["Microsoft.MSPaint"] = new("Paint 3D", WindowsAppAdvice.Safe, "Le Paint classique est une autre application."),
        ["Microsoft.Wallet"] = new("Microsoft Pay", WindowsAppAdvice.Safe),
        ["Microsoft.Messaging"] = new("Messages", WindowsAppAdvice.Safe),
        ["Microsoft.OneConnect"] = new("Forfaits mobiles", WindowsAppAdvice.Safe),
        ["Microsoft.WindowsSoundRecorder"] = new("Enregistreur vocal", WindowsAppAdvice.Safe),
        ["Microsoft.PowerAutomateDesktop"] = new("Power Automate", WindowsAppAdvice.Safe),
        ["Microsoft.MicrosoftJournal"] = new("Microsoft Journal", WindowsAppAdvice.Safe),
        ["Microsoft.Windows.DevHome"] = new("Dev Home", WindowsAppAdvice.Safe),
        ["Microsoft.Copilot"] = new("Copilot", WindowsAppAdvice.Safe),
        ["Microsoft.MicrosoftPCManager"] = new("Microsoft PC Manager", WindowsAppAdvice.Safe),
        ["MicrosoftCorporationII.MicrosoftFamily"] = new("Microsoft Family Safety", WindowsAppAdvice.Safe),
        ["MicrosoftTeams"] = new("Microsoft Teams (personnel)", WindowsAppAdvice.Safe, "Version grand public préinstallée, distincte de Teams pour le travail."),
        ["SpotifyAB.SpotifyMusic"] = new("Spotify", WindowsAppAdvice.Safe, NoteThirdParty),
        ["Disney.37853FC22B2CE"] = new("Disney+", WindowsAppAdvice.Safe, NoteThirdParty),
        ["BytedancePte.Ltd.TikTok"] = new("TikTok", WindowsAppAdvice.Safe, NoteThirdParty),
        ["Facebook.Facebook"] = new("Facebook", WindowsAppAdvice.Safe, NoteThirdParty),
        ["Facebook.InstagramBeta"] = new("Instagram", WindowsAppAdvice.Safe, NoteThirdParty),
        ["Facebook.317180B0BB486"] = new("Instagram", WindowsAppAdvice.Safe, NoteThirdParty),
        ["4DF9E0F8.Netflix"] = new("Netflix", WindowsAppAdvice.Safe, NoteThirdParty),
        ["AmazonVideo.PrimeVideo"] = new("Prime Video", WindowsAppAdvice.Safe, NoteThirdParty),
        ["5A894077.McAfeeSecurity"] = new("McAfee", WindowsAppAdvice.Safe, NoteThirdParty),
        ["AdobeSystemsIncorporated.AdobePhotoshopExpress"] = new("Adobe Photoshop Express", WindowsAppAdvice.Safe, NoteThirdParty),
        ["22364Disney.ESPNBetaPWA"] = new("ESPN", WindowsAppAdvice.Safe, NoteThirdParty),
        ["5319275A.WhatsAppDesktop"] = new("WhatsApp", WindowsAppAdvice.Know, "Supprimez-la seulement si vous ne l'utilisez pas."),
        ["Flipboard.Flipboard"] = new("Flipboard", WindowsAppAdvice.Safe, NoteThirdParty),
        ["9E2F88E3.Twitter"] = new("Twitter", WindowsAppAdvice.Safe, NoteThirdParty),
        ["PandoraMediaInc.29680B314EFC2"] = new("Pandora", WindowsAppAdvice.Safe, NoteThirdParty),
        ["Microsoft.BingTranslator"] = new("Traducteur", WindowsAppAdvice.Safe),

        // ---- À savoir ----
        ["Microsoft.XboxApp"] = new("Xbox Console Companion", WindowsAppAdvice.Know, NoteXbox),
        ["Microsoft.GamingApp"] = new("Xbox", WindowsAppAdvice.Know, NoteXbox),
        ["Microsoft.XboxGamingOverlay"] = new("Xbox Game Bar", WindowsAppAdvice.Know, "Capture d'écran et enregistrement des jeux (Win + G)."),
        ["Microsoft.XboxGameOverlay"] = new("Xbox Game Bar (module)", WindowsAppAdvice.Know, NoteXbox),
        ["Microsoft.XboxIdentityProvider"] = new("Connexion Xbox", WindowsAppAdvice.Know, NoteXbox),
        ["Microsoft.XboxSpeechToTextOverlay"] = new("Xbox (transcription vocale)", WindowsAppAdvice.Know, NoteXbox),
        ["Microsoft.Xbox.TCUI"] = new("Xbox (interface de jeu)", WindowsAppAdvice.Know, NoteXbox),
        ["Microsoft.GamingServices"] = new("Services de jeu", WindowsAppAdvice.Know, NoteXbox),
        ["Microsoft.549981C3F5F10"] = new("Cortana", WindowsAppAdvice.Know, "Assistant vocal, peu utile aujourd'hui."),
        ["Microsoft.Office.OneNote"] = new("OneNote", WindowsAppAdvice.Know, "Vos notes synchronisées restent dans votre compte OneDrive."),
        ["MSTeams"] = new("Microsoft Teams", WindowsAppAdvice.Know, "Gardez-la si vous l'utilisez pour le travail ou l'école."),
        ["microsoft.windowscommunicationsapps"] = new("Courrier et Calendrier", WindowsAppAdvice.Know, "Gardez-la si vous lisez vos mails avec."),
        ["Microsoft.OutlookForWindows"] = new("Outlook (nouveau)", WindowsAppAdvice.Know, "Gardez-la si vous lisez vos mails avec."),
        ["Microsoft.Windows.Photos"] = new("Photos", WindowsAppAdvice.Know, "Visionneuse d'images par défaut : prévoyez une autre visionneuse avant de la supprimer."),
        ["Microsoft.WindowsCamera"] = new("Caméra", WindowsAppAdvice.Know, "Utile pour la webcam."),
        ["Microsoft.WindowsAlarms"] = new("Horloge (alarmes et minuteur)", WindowsAppAdvice.Know),
        ["Microsoft.MicrosoftStickyNotes"] = new("Pense-bêtes", WindowsAppAdvice.Know, "Vos pense-bêtes non synchronisés seraient perdus."),
        ["MicrosoftCorporationII.QuickAssist"] = new("Assistance rapide", WindowsAppAdvice.Know, "Permet à un proche de vous dépanner à distance."),
        ["MicrosoftWindows.CrossDevice"] = new("Expérience multi-appareils", WindowsAppAdvice.Know, "Utilisé par Mobile connecté."),
        ["MicrosoftWindows.Client.WebExperience"] = new("Widgets", WindowsAppAdvice.Know, "Le panneau d'actualités et de météo de la barre des tâches."),
        ["Microsoft.WindowsSoundRecorder.Legacy"] = new("Enregistreur vocal", WindowsAppAdvice.Know),
        ["Microsoft.Windows.Ai.Copilot.Provider"] = new("Copilot (intégration Windows)", WindowsAppAdvice.Know),

        // ---- À garder ----
        ["Microsoft.WindowsStore"] = new("Microsoft Store", WindowsAppAdvice.Keep, NoteStore, Locked: true),
        ["Microsoft.StorePurchaseApp"] = new("Achats du Microsoft Store", WindowsAppAdvice.Keep, NoteStore, Locked: true),
        ["Microsoft.DesktopAppInstaller"] = new("Programme d'installation d'application (winget)", WindowsAppAdvice.Keep,
            "Installe les applications et fournit la commande winget.", Locked: true),
        ["Microsoft.Winget.Source"] = new("Source winget", WindowsAppAdvice.Keep, "Catalogue utilisé par la commande winget.", Locked: true),
        ["Microsoft.MicrosoftEdge.Stable"] = new("Microsoft Edge", WindowsAppAdvice.Keep, "Windows et de nombreuses applications en ont besoin.", Locked: true),
        ["Microsoft.MicrosoftEdge"] = new("Microsoft Edge", WindowsAppAdvice.Keep, "Windows et de nombreuses applications en ont besoin.", Locked: true),
        ["Microsoft.SecHealthUI"] = new("Sécurité Windows", WindowsAppAdvice.Keep, "Interface de l'antivirus intégré.", Locked: true),
        ["Microsoft.WindowsTerminal"] = new("Terminal", WindowsAppAdvice.Keep),
        ["Microsoft.WindowsCalculator"] = new("Calculatrice", WindowsAppAdvice.Keep),
        ["Microsoft.WindowsNotepad"] = new("Bloc-notes", WindowsAppAdvice.Keep),
        ["Microsoft.Paint"] = new("Paint", WindowsAppAdvice.Keep),
        ["Microsoft.ScreenSketch"] = new("Outil Capture d'écran", WindowsAppAdvice.Keep, "Utilisé par le raccourci Win + Maj + S."),
        ["Microsoft.OneDriveSync"] = new("OneDrive", WindowsAppAdvice.Keep),
        ["Microsoft.ApplicationCompatibilityEnhancements"] = new("Améliorations de compatibilité", WindowsAppAdvice.Keep),
        ["Microsoft.HEIFImageExtension"] = new("Extension d'images HEIF", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.HEVCVideoExtension"] = new("Extension vidéo HEVC", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.VP9VideoExtensions"] = new("Extension vidéo VP9", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.AV1VideoExtension"] = new("Extension vidéo AV1", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.MPEG2VideoExtension"] = new("Extension vidéo MPEG-2", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.WebMediaExtensions"] = new("Extensions multimédias web", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.WebpImageExtension"] = new("Extension d'images WebP", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.RawImageExtension"] = new("Extension d'images RAW", WindowsAppAdvice.Keep, NoteCodec),
        ["Microsoft.AVCEncoderVideoExtension"] = new("Extension vidéo AVC", WindowsAppAdvice.Keep, NoteCodec),
    };

    // Bibliothèques partagées par d'autres applications : jamais affichées.
    private static readonly string[] HiddenPrefixes =
    {
        "Microsoft.VCLibs", "Microsoft.UI.Xaml", "Microsoft.NET.Native", "Microsoft.WindowsAppRuntime",
        "Microsoft.Services.Store.Engagement", "Microsoft.DirectXRuntime", "Microsoft.Advertising.Xaml",
        "MicrosoftWindows.Client.", "Microsoft.Windows.", "Windows.", "Microsoft.AAD.", "Microsoft.AccountsControl",
        "Microsoft.LockApp", "Microsoft.ECApp", "Microsoft.CredDialogHost", "Microsoft.BioEnrollment", "Microsoft.Win32WebViewHost",
        "Microsoft.AsyncTextService", "Microsoft.WidgetsPlatformRuntime", "Microsoft.LanguageExperiencePack",
        "MicrosoftWindows.UndockedDevKit", "NcsiUwpApp", "c5e2524a-ea46-4f67-841f-6a9465d9d515", "1527c705-839a-4832-9118-54d4Bd6a0c89",
        "E2A4F912-2574-4A75-9BB0-0D023378592B", "F46D4000-FD22-4DB4-AC8E-4E1DDDE828FE",
    };

    private static readonly Regex SafeFullName = new(@"^[A-Za-z0-9._~\-]+$", RegexOptions.Compiled);

    /// <summary>Lit les applications installées pour l'utilisateur actuel. Liste vide si PowerShell ne renvoie rien.</summary>
    public static List<WindowsApp> Load()
    {
        const string script =
            "$ErrorActionPreference = 'Stop'\n" +
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
            "$p = @(Get-AppxPackage | Select-Object Name, PackageFullName, Publisher, @{n='Version';e={[string]$_.Version}}, " +
            "InstallLocation, IsFramework, NonRemovable, @{n='SignatureKind';e={[string]$_.SignatureKind}})\n" +
            "if ($p.Count -eq 0) { '[]' } else { ConvertTo-Json -InputObject $p -Compress -Depth 2 }";

        var (exitCode, output, error) = RunPowerShell(script, TimeSpan.FromMinutes(2));
        if (exitCode != 0)
            throw new InvalidOperationException(FirstLine(error) ?? L.F("PowerShell a échoué (code {0}).", exitCode));

        var json = output.Trim();
        if (json.Length == 0)
            return new List<WindowsApp>();

        using var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.ValueKind switch
        {
            JsonValueKind.Array => doc.RootElement.EnumerateArray().ToList(),
            JsonValueKind.Object => new List<JsonElement> { doc.RootElement },
            _ => new List<JsonElement>(),
        };

        var packages = new List<(string Name, string FullName, string? Publisher, string? Version, string? Location)>();
        foreach (var e in elements)
        {
            if (e.ValueKind != JsonValueKind.Object)
                continue;
            var name = Str(e, "Name");
            var fullName = Str(e, "PackageFullName");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(fullName))
                continue;
            if (Bool(e, "IsFramework") || Bool(e, "NonRemovable"))
                continue;
            if (string.Equals(Str(e, "SignatureKind"), "System", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Table.ContainsKey(name) && HiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;
            packages.Add((name, fullName, Str(e, "Publisher"), Str(e, "Version"), Str(e, "InstallLocation")));
        }

        var apps = new List<WindowsApp>();
        foreach (var group in packages.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var publisher = PublisherName(first.Publisher);
            var isMicrosoft = first.Name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)
                              || (first.Publisher?.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) ?? false);
            var known = Classify(first.Name);
            apps.Add(new WindowsApp
            {
                Name = first.Name,
                PackageFullNames = group.Select(p => p.FullName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                FrenchName = known?.Name ?? GuessName(first.Name),
                Publisher = publisher,
                Version = first.Version,
                InstallLocation = first.Location,
                IsMicrosoft = isMicrosoft,
                Advice = known?.Advice ?? WindowsAppAdvice.Check,
                FrenchNote = known is not null ? known.Note : isMicrosoft ? NoteUnknown : NoteOtherPublisher,
                Locked = known?.Locked ?? false,
            });
        }

        return apps.OrderBy(a => a.Advice)
                   .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                   .ToList();
    }

    /// <summary>
    /// Supprime un paquet pour l'utilisateur actuel seulement (Remove-AppxPackage sans -AllUsers).
    /// Renvoie null si tout s'est bien passé, sinon le message d'erreur.
    /// </summary>
    public static string? Remove(string packageFullName)
    {
        if (!SafeFullName.IsMatch(packageFullName))
            return L.T("Nom de paquet inattendu : suppression refusée.");

        var script =
            "$ErrorActionPreference = 'Stop'\n" +
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
            $"try {{ Remove-AppxPackage -Package '{packageFullName}' -ErrorAction Stop; exit 0 }} " +
            "catch { [Console]::Out.WriteLine($_.Exception.Message); exit 1 }";

        try
        {
            var (exitCode, output, error) = RunPowerShell(script, TimeSpan.FromMinutes(5));
            if (exitCode == 0)
                return null;
            return FirstLine(output) ?? FirstLine(error) ?? L.F("Échec (code {0}).", exitCode);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static Known? Classify(string name)
    {
        if (Table.TryGetValue(name, out var known))
            return known;
        if (name.StartsWith("king.com.", StringComparison.OrdinalIgnoreCase))
            return new Known(SplitWords(name["king.com.".Length..]), WindowsAppAdvice.Safe, "Jeu King (Candy Crush…) installé d'office.");
        return null;
    }

    /// <summary>Nom lisible deviné à partir du nom du paquet (« 5319275A.WhatsAppDesktop » → « WhatsApp Desktop »).</summary>
    private static string GuessName(string name)
    {
        var dot = name.IndexOf('.');
        var rest = dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
        return SplitWords(rest.Replace('.', ' '));
    }

    private static string SplitWords(string s) =>
        Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ").Trim();

    /// <summary>« CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond… » → « Microsoft Corporation ».</summary>
    private static string? PublisherName(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
            return null;
        string? cn = null;
        foreach (var part in publisher.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("O=", StringComparison.OrdinalIgnoreCase))
                return p[2..].Trim('"', ' ');
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                cn = p[3..].Trim('"', ' ');
        }
        // Un CN qui ressemble à un identifiant (GUID) n'aide personne.
        return cn is not null && !Regex.IsMatch(cn, @"^[0-9A-Fa-f\-]{20,}$") ? cn : null;
    }

    private static string? Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? FirstLine(string? text) =>
        text?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    /// <summary>Lance Windows PowerShell (5.1, qui a toujours le module Appx) caché, sans profil.</summary>
    private static (int ExitCode, string Output, string Error) RunPowerShell(string script, TimeSpan timeout)
    {
        var system = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
            : Environment.SystemDirectory;
        var exe = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(exe))
            exe = "powershell.exe";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo(exe, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException(L.T("PowerShell n'a pas démarré."));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
            throw new TimeoutException(L.T("PowerShell ne répond pas : opération abandonnée."));
        }
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, CleanError(stderr.Result));
    }

    /// <summary>PowerShell renvoie parfois sa barre de progression en CLIXML sur la sortie d'erreur : on l'ignore.</summary>
    private static string CleanError(string error) =>
        error.Contains("#< CLIXML", StringComparison.Ordinal) && !error.Contains("S=\"Error\"", StringComparison.Ordinal)
            ? string.Empty
            : Regex.Replace(error, "<[^>]+>", " ").Replace("#< CLIXML", "").Replace("_x000D__x000A_", " ").Trim();
}
