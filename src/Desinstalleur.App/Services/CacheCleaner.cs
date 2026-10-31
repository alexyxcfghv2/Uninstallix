using System.Diagnostics;
using System.Runtime.InteropServices;
using Desinstalleur.App.Models;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>
/// Liste des caches connus et nettoyage de leur contenu.
/// Seuls des dossiers de cache précis sont vidés : jamais de réglages, de sauvegardes ni de jeux.
/// Les fichiers utilisés par un programme ouvert sont simplement ignorés.
/// </summary>
public static class CacheCleaner
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static string SystemDrive => Path.GetPathRoot(WindowsDir) is { Length: > 0 } root ? root : @"C:\";

    /// <summary>
    /// Données de connexion des navigateurs intégrés (Chromium/CEF) : gardées pour ne pas déconnecter le compte.
    /// </summary>
    private static readonly string[] ChromiumLoginData =
    {
        "Cookies", "Cookies-journal", "Network", "Local Storage", "Session Storage", "IndexedDB",
        "Login Data", "Login Data-journal", "Web Data", "Web Data-journal",
    };

    /// <summary>Numéro de profil cleanmgr /sagerun réservé à Uninstallix (valeurs StateFlagsNNNN).</summary>
    private const int SageProfile = 4271;

    public static List<CacheTarget> BuildCatalog()
    {
        var fivem = Path.Combine(Local, "FiveM", "FiveM.app");
        return new List<CacheTarget>
        {
            new()
            {
                Name = "Fichiers temporaires",
                Purpose = "Fichiers de travail créés par Windows, les installateurs et les programmes. Une fois la tâche terminée, ils ne servent généralement plus.",
                Consequence = "Rien à refaire : les programmes recréent ces fichiers s'ils en ont besoin. Les fichiers encore ouverts par un programme sont ignorés.",
                Impact = CacheImpact.None,
                Description = "Fichiers laissés par Windows et les installations. Les fichiers de moins de 24 h sont gardés (réglable).",
                Glyph = "",
                Folders = new[] { Path.GetTempPath(), Path.Combine(WindowsDir, "Temp") },
                SkipRecentFiles = true,
                IsSelected = true,
            },
            new()
            {
                Name = "FiveM",
                Purpose = "Ressources téléchargées depuis les serveurs (véhicules, cartes, vêtements, scripts), ainsi que les journaux et rapports de plantage de FiveM.",
                Consequence = "Les ressources de chaque serveur seront téléchargées de nouveau à la prochaine connexion : la première arrivée sur un serveur sera plus longue. Les fichiers du jeu (dossier « game »), votre compte et vos réglages ne sont pas touchés.",
                Impact = CacheImpact.Notice,
                Description = "Cache des serveurs, journaux et rapports de plantage. Les fichiers du jeu ne sont pas touchés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(fivem, "data", "cache"),
                    Path.Combine(fivem, "data", "server-cache"),
                    Path.Combine(fivem, "data", "server-cache-priv"),
                    Path.Combine(fivem, "crashes"),
                    Path.Combine(fivem, "logs"),
                },
                KeepSubfolders = new[] { "game" },
                Processes = new[] { "FiveM", "FiveM_GTAProcess", "FiveM_b3258_GTAProcess", "FiveM_ChromeBrowser" },
                AppName = "FiveM",
                IsSelected = true,
            },
            new()
            {
                Name = "Discord",
                Purpose = "Copies locales des images, avatars, émojis et pièces jointes déjà affichés, et code de l'application préparé pour démarrer plus vite.",
                Consequence = "Discord retélécharge les images et émojis au fil de la navigation et peut démarrer un peu plus lentement la première fois. Vous restez connecté ; messages, serveurs et réglages ne sont pas touchés.",
                Impact = CacheImpact.Low,
                Description = "Images et données mises en cache. Vos messages et votre compte ne sont pas touchés.",
                Glyph = "",
                Folders = new[] { "Cache", "Code Cache", "GPUCache" }.Select(f => Path.Combine(Roaming, "discord", f)).ToArray(),
                Processes = new[] { "Discord" },
                AppName = "Discord",
                IsSelected = true,
            },
            new()
            {
                Name = "Google Chrome",
                Purpose = "Copies des pages, images et scripts des sites déjà visités (dossiers Cache, Code Cache et GPUCache de chaque profil), pour les afficher plus vite.",
                Consequence = "Les sites seront un peu plus lents à charger la première fois, le temps de retélécharger leurs fichiers. Les cookies ne sont pas supprimés : vous restez connecté aux sites. Historique, mots de passe, favoris et onglets sont conservés.",
                Impact = CacheImpact.Low,
                Description = "Cache des pages web. Historique, mots de passe et onglets sont conservés.",
                Glyph = "",
                Folders = BrowserCaches(Path.Combine(Local, "Google", "Chrome", "User Data")),
                Processes = new[] { "chrome" },
                AppName = "Google Chrome",
            },
            new()
            {
                Name = "Microsoft Edge",
                Purpose = "Copies des pages, images et scripts des sites déjà visités (dossiers Cache, Code Cache et GPUCache de chaque profil), pour les afficher plus vite.",
                Consequence = "Les sites seront un peu plus lents à charger la première fois, le temps de retélécharger leurs fichiers. Les cookies ne sont pas supprimés : vous restez connecté aux sites. Historique, mots de passe, favoris et onglets sont conservés.",
                Impact = CacheImpact.Low,
                Description = "Cache des pages web. Historique, mots de passe et onglets sont conservés.",
                Glyph = "",
                Folders = BrowserCaches(Path.Combine(Local, "Microsoft", "Edge", "User Data")),
                Processes = new[] { "msedge" },
                AppName = "Microsoft Edge",
            },
            new()
            {
                Name = "Steam",
                Purpose = "Cache du navigateur intégré de Steam : pages du magasin, de la communauté et de la bibliothèque déjà affichées.",
                Consequence = "Le magasin et la communauté se rechargeront un peu plus lentement la première fois. Vos jeux, vos sauvegardes et les téléchargements en cours ne sont pas touchés.",
                Impact = CacheImpact.Low,
                Description = "Cache du navigateur intégré de Steam. Vos jeux ne sont pas touchés.",
                Glyph = "",
                Folders = new[] { Path.Combine(Local, "Steam", "htmlcache") },
                Processes = new[] { "steam", "steamwebhelper" },
                AppName = "Steam",
            },
            new()
            {
                Name = "Roblox",
                Purpose = "Journaux d'activité écrits par Roblox et Roblox Studio à chaque session. Ils ne servent qu'à diagnostiquer un problème.",
                Consequence = "Aucun effet sur le jeu : votre compte, vos réglages et vos créations ne sont pas touchés. Les journaux des sessions passées ne seront plus disponibles pour un dépannage.",
                Impact = CacheImpact.None,
                Description = "Journaux de Roblox et de Roblox Studio.",
                Glyph = "",
                Folders = new[] { Path.Combine(Local, "Roblox", "logs") },
                Processes = new[] { "RobloxPlayerBeta", "RobloxStudioBeta" },
                AppName = "Roblox",
            },
            new()
            {
                Name = "Rockstar Games Launcher",
                Purpose = "Cache du navigateur intégré du Rockstar Games Launcher (pages de la boutique, images, scripts, cache graphique) et journaux du launcher.",
                Consequence = "Le launcher retélécharge ses pages et images au prochain démarrage, qui peut être un peu plus long. Utile quand le launcher reste bloqué, affiche une page blanche ou ne se connecte pas. Vos jeux (GTA V, Red Dead…), vos sauvegardes et vos réglages ne sont pas touchés, et les cookies sont gardés : vous restez connecté.",
                Impact = CacheImpact.Low,
                Description = "Cache et journaux du launcher. Vos jeux, sauvegardes et votre compte ne sont pas touchés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "Rockstar Games", "Launcher", "cache"),
                    Path.Combine(Local, "Rockstar Games", "Launcher", "logs"),
                },
                // Données de connexion du navigateur intégré : on les garde pour ne pas déconnecter le compte.
                KeepSubfolders = new[] { "Cookies", "Cookies-journal", "Network", "Local Storage", "Session Storage", "IndexedDB" },
                Processes = new[] { "Launcher@Rockstar Games", "LauncherPatcher@Rockstar Games", "SocialClubHelper" },
                AppName = "Rockstar Games Launcher",
            },
            new()
            {
                Name = "Epic Games Launcher",
                Purpose = "Cache du navigateur intégré de l'Epic Games Launcher (pages de la boutique, images, scripts) et journaux du launcher.",
                Consequence = "Le launcher retélécharge ses pages et images au prochain démarrage, qui peut être un peu plus long. C'est la solution conseillée par Epic quand la boutique s'affiche mal ou reste bloquée. Vos jeux (Fortnite…), vos sauvegardes et vos réglages ne sont pas touchés, et les cookies sont gardés : vous restez connecté.",
                Impact = CacheImpact.Low,
                Description = "Cache web et journaux du launcher. Vos jeux, sauvegardes et votre compte ne sont pas touchés.",
                Glyph = "",
                Folders = EpicFolders(),
                KeepSubfolders = ChromiumLoginData,
                Processes = new[] { "EpicGamesLauncher", "EpicWebHelper" },
                AppName = "Epic Games Launcher",
            },
            new()
            {
                Name = "Battle.net",
                Purpose = "Cache du navigateur intégré de l'application Battle.net (pages, images, scripts) et journaux du launcher.",
                Consequence = "Battle.net recharge ses pages au prochain démarrage, qui peut être un peu plus long. Utile quand l'application reste bloquée sur « Chargement des informations du compte ». Vos jeux, vos sauvegardes et vos réglages ne sont pas touchés, et les cookies sont gardés : vous restez connecté.",
                Impact = CacheImpact.Low,
                Description = "Cache web et journaux de Battle.net. Vos jeux et votre compte ne sont pas touchés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "Battle.net", "Cache"),
                    Path.Combine(Local, "Blizzard Entertainment", "Battle.net", "Logs"),
                    Path.Combine(Roaming, "Battle.net", "Logs"),
                },
                KeepSubfolders = ChromiumLoginData,
                Processes = new[] { "Battle.net", "Agent@Battle.net" },
                AppName = "Battle.net",
            },
            new()
            {
                Name = "EA app",
                Purpose = "Cache de l'interface de l'EA app (boutique, bibliothèque), cache de l'overlay en jeu et journaux de l'application.",
                Consequence = "L'EA app recharge ses pages au prochain démarrage, qui peut être un peu plus long. Vos jeux, vos sauvegardes et vos réglages ne sont pas touchés, et les cookies sont gardés : vous restez connecté. Pour une réparation plus complète, EA propose aussi « Récupération de l'application » dans le menu Aide.",
                Impact = CacheImpact.Low,
                Description = "Cache et journaux de l'EA app. Vos jeux, sauvegardes et votre compte ne sont pas touchés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "EADesktop", "cache"),
                    Path.Combine(Local, "Electronic Arts", "EA Desktop", "IGOCache"),
                    Path.Combine(Local, "Electronic Arts", "EA Desktop", "Logs"),
                },
                KeepSubfolders = ChromiumLoginData,
                Processes = new[] { "EADesktop" },
                AppName = "EA app",
            },
            new()
            {
                Name = "Ubisoft Connect",
                Purpose = "Cache du navigateur intégré d'Ubisoft Connect (boutique, actualités, overlay) et journaux du launcher.",
                Consequence = "Ubisoft Connect recharge ses pages au prochain démarrage, qui peut être un peu plus long. Vos jeux, vos sauvegardes (dossier « savegames ») et vos réglages ne sont pas touchés, et les cookies sont gardés : vous restez connecté.",
                Impact = CacheImpact.Low,
                Description = "Cache web et journaux d'Ubisoft Connect. Vos jeux et sauvegardes ne sont pas touchés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "Ubisoft Game Launcher", "cache"),
                    Path.Combine(Local, "Ubisoft Game Launcher", "logs"),
                    Path.Combine(ProgramFilesX86, "Ubisoft", "Ubisoft Game Launcher", "logs"),
                },
                KeepSubfolders = ChromiumLoginData,
                Processes = new[] { "UbisoftConnect", "upc", "UplayWebCore" },
                AppName = "Ubisoft Connect",
            },
            new()
            {
                Name = "Riot Client",
                Purpose = "Journaux et rapports de plantage du Riot Client, de VALORANT et de League of Legends. Ils ne servent qu'à diagnostiquer un problème.",
                Consequence = "Aucun effet sur les jeux : votre compte, vos réglages, vos raccourcis et vos replays ne sont pas touchés, ni Vanguard. Les journaux des sessions passées ne seront plus disponibles pour un dépannage.",
                Impact = CacheImpact.None,
                Description = "Journaux du Riot Client, de VALORANT et de League of Legends.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "Riot Games", "Riot Client", "Logs"),
                    Path.Combine(Local, "VALORANT", "Saved", "Logs"),
                    Path.Combine(Local, "VALORANT", "Saved", "Crashes"),
                    Path.Combine(SystemDrive, "Riot Games", "League of Legends", "Logs"),
                },
                Processes = new[] { "RiotClientServices", "RiotClientUx", "RiotClientUxRender", "Riot Client", "VALORANT-Win64-Shipping", "LeagueClient", "LeagueClientUx", "League of Legends" },
                AppName = "Riot Client",
            },
            new()
            {
                Name = "Minecraft",
                Purpose = "Journaux et rapports de plantage de Minecraft Java Edition, et cache du navigateur intégré du launcher (actualités, pages du compte).",
                Consequence = "Aucun effet sur le jeu : vos mondes, packs de ressources, mods, captures d'écran et réglages ne sont pas touchés. Le launcher recharge ses actualités au prochain démarrage et les cookies sont gardés : vous restez connecté.",
                Impact = CacheImpact.None,
                Description = "Journaux, rapports de plantage et cache du launcher. Vos mondes ne sont pas touchés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Roaming, ".minecraft", "logs"),
                    Path.Combine(Roaming, ".minecraft", "crash-reports"),
                    Path.Combine(Roaming, ".minecraft", "webcache2"),
                },
                KeepSubfolders = ChromiumLoginData,
                Processes = new[] { "MinecraftLauncher", "Minecraft@Microsoft.4297127D64EC6", "javaw@.minecraft", "javaw@Microsoft.4297127D64EC6" },
                AppName = "Minecraft",
            },
            new()
            {
                Name = "Spotify",
                Purpose = "Morceaux déjà écoutés gardés en cache pour être relus sans les retélécharger, et cache du navigateur intégré de Spotify.",
                Consequence = "Les morceaux seront de nouveau téléchargés en streaming quand vous les écouterez : un peu plus de données utilisées au début. Vos playlists, votre compte et vos réglages ne sont pas touchés ; les titres téléchargés pour l'écoute hors ligne (dossier « Storage ») sont normalement gardés, mais dans de rares cas il faudra les retélécharger.",
                Impact = CacheImpact.Low,
                Description = "Cache de lecture et cache web. Vos playlists et vos téléchargements hors ligne sont conservés.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "Spotify", "Data"),
                    Path.Combine(Local, "Spotify", "Browser"),
                    Path.Combine(Local, "Packages", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "LocalCache", "Spotify", "Data"),
                },
                KeepSubfolders = ChromiumLoginData,
                Processes = new[] { "Spotify" },
                AppName = "Spotify",
            },
            new()
            {
                Name = "Microsoft Teams",
                Purpose = "Cache de l'interface de Teams (images, avatars, scripts, cache graphique) et journaux de diagnostic, pour le nouveau Teams comme pour l'ancien.",
                Consequence = "Teams recharge images et avatars au prochain démarrage, qui peut être un peu plus long. Vos conversations, équipes et fichiers sont sur les serveurs Microsoft et ne sont pas touchés ; vous restez connecté. Les journaux de diagnostic des sessions passées seront supprimés.",
                Impact = CacheImpact.Low,
                Description = "Cache et journaux de Teams. Vos conversations et votre compte ne sont pas touchés.",
                Glyph = "",
                Folders = TeamsFolders(),
                Processes = new[] { "ms-teams", "Teams@Microsoft\\Teams" },
                AppName = "Microsoft Teams",
            },
            new()
            {
                Name = "Cache des shaders",
                Purpose = "Shaders déjà compilés par les pilotes graphiques NVIDIA et AMD et par DirectX, pour ne pas les recalculer à chaque lancement de jeu.",
                Consequence = "Ils sont reconstruits automatiquement : les premiers lancements de vos jeux peuvent être plus longs ou saccader brièvement, le temps de les recompiler. Utile après une mise à jour du pilote graphique ou en cas de bugs d'affichage.",
                Impact = CacheImpact.Notice,
                Description = "Shaders NVIDIA, AMD et DirectX. Ils seront recréés : les jeux peuvent saccader un peu au premier lancement.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "NVIDIA", "DXCache"),
                    Path.Combine(Local, "NVIDIA", "GLCache"),
                    Path.Combine(Local, "AMD", "DxCache"),
                    Path.Combine(Local, "AMD", "DxcCache"),
                    Path.Combine(Local, "D3DSCache"),
                },
            },
            new()
            {
                Name = "Rapports d'erreurs",
                Purpose = "Rapports de plantage et vidages mémoire gardés par Windows pour un éventuel envoi à Microsoft ou aux développeurs.",
                Consequence = "Aucun effet sur le fonctionnement du PC. Les plantages passés ne pourront simplement plus être analysés ni envoyés.",
                Impact = CacheImpact.None,
                Description = "Rapports de plantage et vidages mémoire conservés par Windows.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(Local, "CrashDumps"),
                    Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(ProgramData, "Microsoft", "Windows", "WER", "ReportQueue"),
                },
                IsSelected = true,
            },
            new()
            {
                Name = "Cache des miniatures",
                Purpose = "Aperçus des photos, vidéos et documents que l'Explorateur Windows affiche dans les dossiers (fichiers thumbcache).",
                Consequence = "Les miniatures sont recréées au fil de la navigation : les dossiers remplis d'images s'affichent un peu plus lentement la première fois. Utile quand des miniatures sont fausses ou ne s'affichent plus. Les fichiers utilisés par l'Explorateur sont ignorés : redémarrez le PC puis relancez le nettoyage pour tout vider.",
                Impact = CacheImpact.Low,
                Description = "Aperçus des images et vidéos affichés par l'Explorateur. Ils seront recréés.",
                Glyph = "",
                Folders = new[] { Path.Combine(Local, "Microsoft", "Windows", "Explorer") },
                FilePattern = "thumbcache_*.db",
            },
            new()
            {
                Name = "Optimisation de livraison",
                Purpose = "Copies des mises à jour Windows et des applications du Microsoft Store gardées pour être partagées avec d'autres PC (réseau local ou Internet).",
                Consequence = "Aucun effet sur les mises à jour déjà installées. Votre PC ne pourra simplement plus partager ces fichiers avec d'autres appareils ; Windows en regardera de nouveaux au fil des mises à jour.",
                Impact = CacheImpact.None,
                Description = "Fichiers de mises à jour conservés pour être partagés avec d'autres PC.",
                Glyph = "",
                Folders = new[]
                {
                    Path.Combine(WindowsDir, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"),
                    Path.Combine(WindowsDir, "SoftwareDistribution", "DeliveryOptimization"),
                },
                Special = CacheSpecial.DeliveryOptimization,
            },
            new()
            {
                Name = "Téléchargements Windows Update",
                Purpose = "Fichiers des mises à jour Windows déjà téléchargés (dossier « Download » de SoftwareDistribution). Une fois les mises à jour installées, ils ne servent plus.",
                Consequence = "Les mises à jour déjà installées ne sont pas touchées : elles restent désinstallables. Une mise à jour en attente sera simplement retéléchargée. Le service Windows Update est arrêté le temps du nettoyage puis redémarré. Évitez de lancer ce nettoyage pendant l'installation d'une mise à jour.",
                Impact = CacheImpact.Notice,
                Description = "Mises à jour déjà téléchargées. Celles qui sont en attente seront retéléchargées.",
                Glyph = "",
                Folders = new[] { Path.Combine(WindowsDir, "SoftwareDistribution", "Download") },
                StopServices = new[] { "wuauserv", "bits" },
                Warning = "Windows Update et le service de transfert intelligent (BITS) seront arrêtés pendant le nettoyage, puis redémarrés.",
            },
            new()
            {
                Name = "Ancienne version de Windows",
                Purpose = "Copie de votre ancienne installation de Windows (dossier Windows.old), gardée après une mise à niveau pour pouvoir revenir en arrière. Windows la supprime en général tout seul après 10 jours.",
                Consequence = "Le retour à la version précédente de Windows deviendra impossible. Les fichiers personnels éventuellement restés dans Windows.old seront supprimés eux aussi. Le nettoyage est fait par l'outil de nettoyage de disque de Windows, qui s'ouvre quelques minutes.",
                Impact = CacheImpact.Permanent,
                Description = "Dossier Windows.old laissé par une mise à niveau. Supprimé définitivement : plus de retour en arrière possible.",
                Glyph = "",
                Folders = new[] { Path.Combine(SystemDrive, "Windows.old") },
                Special = CacheSpecial.WindowsOld,
                Warning = "Windows.old sera supprimé définitivement : vous ne pourrez plus revenir à la version précédente de Windows. Vérifiez qu'il ne contient plus de fichiers personnels à garder.",
            },
            new()
            {
                Name = "Corbeille",
                Purpose = "Fichiers que vous avez supprimés, gardés au cas où vous voudriez les restaurer.",
                Consequence = "Ces fichiers seront supprimés définitivement : ils ne pourront plus être restaurés depuis la Corbeille.",
                Impact = CacheImpact.Permanent,
                Description = "Vide définitivement la Corbeille de tous les lecteurs.",
                Glyph = "",
                IsRecycleBin = true,
            },
        };
    }

    /// <summary>Caches web de l'Epic Games Launcher (webcache, webcache_4147, webcache_4430… selon la version) et journaux.</summary>
    private static string[] EpicFolders()
    {
        var folders = new List<string>();
        // Epic écrit « EpicGamesLauncher » ; certaines pages d'aide écrivent « Epic Games Launcher ».
        foreach (var saved in new[] { Path.Combine(Local, "EpicGamesLauncher", "Saved"), Path.Combine(Local, "Epic Games Launcher", "Saved") })
        {
            try
            {
                if (!Directory.Exists(saved))
                    continue;
                folders.AddRange(Directory.GetDirectories(saved, "webcache*"));
                folders.Add(Path.Combine(saved, "Logs"));
            }
            catch
            {
            }
        }
        return folders.ToArray();
    }

    /// <summary>Caches du nouveau Teams (WebView2) et de l'ancien Teams (Electron), et journaux de diagnostic.</summary>
    private static string[] TeamsFolders()
    {
        var folders = new List<string>();
        var classic = Path.Combine(Roaming, "Microsoft", "Teams");
        folders.AddRange(new[] { "Cache", "Code Cache", "GPUCache", "tmp" }.Select(f => Path.Combine(classic, f)));

        var msTeams = Path.Combine(Local, "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams");
        folders.Add(Path.Combine(msTeams, "Logs"));
        var webView = Path.Combine(msTeams, "EBWebView");
        try
        {
            if (Directory.Exists(webView))
            {
                // Profils WebView2 (WV2Profile_tfw, Default…) : seuls leurs caches sont vidés.
                foreach (var profile in Directory.GetDirectories(webView).Prepend(webView))
                    folders.AddRange(new[] { "Cache", "Code Cache", "GPUCache" }.Select(f => Path.Combine(profile, f)));
            }
        }
        catch
        {
        }
        return folders.ToArray();
    }

    /// <summary>Dossiers de cache de chaque profil d'un navigateur basé sur Chromium.</summary>
    private static string[] BrowserCaches(string userData)
    {
        if (!Directory.Exists(userData))
            return Array.Empty<string>();

        var profiles = Directory.GetDirectories(userData)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
            });
        return profiles
            .SelectMany(p => new[] { Path.Combine(p, "Cache"), Path.Combine(p, "Code Cache"), Path.Combine(p, "GPUCache") })
            .ToArray();
    }

    public static long Measure(CacheTarget target)
    {
        if (target.IsRecycleBin)
            return RecycleBinSize();

        long total = 0;
        foreach (var folder in ExistingFolders(target))
        {
            foreach (var file in EnumerateFiles(target, folder))
            {
                if (ShouldSkip(target, folder, file))
                    continue;
                total += SafeLength(file);
            }
        }

        // Le cache de l'Optimisation de livraison appartient au compte NetworkService : s'il est illisible, on demande sa taille à Windows.
        if (total == 0 && target.Special == CacheSpecial.DeliveryOptimization)
            total = DeliveryOptimizationStatus().Bytes;
        return total;
    }

    /// <summary>Noms des logiciels concernés qui sont ouverts en ce moment.</summary>
    public static IEnumerable<string> RunningApps(IEnumerable<CacheTarget> targets) =>
        targets.Where(t => t.AppName is not null && t.Processes.Any(IsRunning))
               .Select(t => t.AppName!)
               .Distinct();

    private static bool IsRunning(string processName)
    {
        var processes = Matching(processName);
        var running = processes.Count > 0;
        foreach (var p in processes)
            p.Dispose();
        return running;
    }

    /// <summary>
    /// Processus correspondant à « nom » ou à « nom@morceau de chemin » : la seconde forme ne garde que les
    /// processus dont l'exe se trouve dans un dossier contenant ce morceau (ex. « Launcher@Rockstar Games »,
    /// pour ne pas confondre avec un autre « Launcher.exe »).
    /// </summary>
    private static List<Process> Matching(string spec)
    {
        var at = spec.IndexOf('@');
        var name = at < 0 ? spec : spec[..at];
        var processes = Process.GetProcessesByName(name).ToList();
        if (at < 0)
            return processes;

        var fragment = spec[(at + 1)..];
        var kept = new List<Process>();
        foreach (var process in processes)
        {
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch
            {
                // Accès refusé : on ne peut pas vérifier, on l'ignore.
            }
            if (path is not null && path.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                kept.Add(process);
            else
                process.Dispose();
        }
        return kept;
    }

    /// <summary>Vide le cache et renvoie le nombre d'octets libérés et de fichiers ignorés.</summary>
    public static (long Freed, int Skipped) Clean(CacheTarget target)
    {
        if (target.IsRecycleBin)
        {
            var size = RecycleBinSize();
            var result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            Journal.Write($"Corbeille vidée (code {result}).");
            return (result == 0 || result == unchecked((int)0x8000FFFF) ? size : 0, 0);
        }

        if (target.Special == CacheSpecial.DeliveryOptimization)
            return CleanDeliveryOptimization(target);
        if (target.Special == CacheSpecial.WindowsOld)
            return CleanWindowsOld(target);

        // Services arrêtés le temps du nettoyage (Windows Update), toujours redémarrés ensuite.
        var stopped = StopServicesFor(target);
        try
        {
            return CleanFolders(target);
        }
        finally
        {
            RestartServices(stopped);
        }
    }

    private static (long Freed, int Skipped) CleanFolders(CacheTarget target)
    {
        long freed = 0;
        var skipped = 0;
        foreach (var folder in ExistingFolders(target))
        {
            if (!IsSafeCacheFolder(folder))
            {
                Journal.Write($"Dossier de cache refusé par sécurité : {folder}");
                continue;
            }

            foreach (var file in EnumerateFiles(target, folder))
            {
                if (ShouldSkip(target, folder, file))
                    continue;
                var length = SafeLength(file);
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    freed += length;
                }
                catch
                {
                    skipped++; // fichier utilisé ou protégé
                }
            }

            // Avec un motif de fichiers, on ne touche qu'aux fichiers visés (pas aux sous-dossiers voisins).
            if (target.FilePattern is null)
                RemoveEmptyDirectories(folder);
        }

        Journal.Write($"Cache « {target.Name} » nettoyé : {freed} octets libérés, {skipped} fichier(s) ignoré(s).");
        return (freed, skipped);
    }

    /// <summary>
    /// Aperçu de ce que le nettoyage supprimerait : un élément par fichier ou sous-dossier
    /// directement dans chaque dossier de cache, avec sa taille et son nombre de fichiers.
    /// </summary>
    public static CachePreview Preview(CacheTarget target)
    {
        var preview = new CachePreview(target);
        if (target.IsRecycleBin)
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBin(null, ref info) == 0 && info.i64NumItems > 0)
                preview.Entries.Add(new PreviewEntry(L.T("Contenu de la Corbeille"), L.T("Corbeille de tous les lecteurs"), info.i64Size, (int)info.i64NumItems, false));
            return preview;
        }

        foreach (var folder in ExistingFolders(target))
        {
            var groups = new Dictionary<string, PreviewEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateFiles(target, folder))
            {
                if (ShouldSkip(target, folder, file))
                {
                    preview.KeptFiles++;
                    continue;
                }
                var first = Path.GetRelativePath(folder, file).Split('\\', '/')[0];
                var path = Path.Combine(folder, first);
                if (!groups.TryGetValue(path, out var entry))
                {
                    var isFolder = !string.Equals(path, file, StringComparison.OrdinalIgnoreCase);
                    entry = new PreviewEntry(first, path, 0, 0, isFolder);
                    groups[path] = entry;
                }
                entry.SizeBytes += SafeLength(file);
                entry.FileCount++;
            }
            preview.Entries.AddRange(groups.Values);
        }
        if (preview.Entries.Count == 0 && target.Special == CacheSpecial.DeliveryOptimization)
        {
            var (count, bytes) = DeliveryOptimizationStatus();
            if (count > 0 || bytes > 0)
                preview.Entries.Add(new PreviewEntry(L.T("Cache de l'Optimisation de livraison"), L.T("Géré par Windows (service DoSvc)"), bytes, Math.Max(count, 1), false));
        }
        preview.Entries.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return preview;
    }

    /// <summary>Demande la fermeture des logiciels concernés (comme un clic sur la croix). Renvoie ceux encore ouverts.</summary>
    public static List<string> CloseApps(IEnumerable<CacheTarget> targets)
    {
        var list = targets.Where(t => t.AppName is not null).ToList();
        foreach (var target in list)
        {
            foreach (var name in target.Processes)
            {
                foreach (var process in Matching(name))
                {
                    try
                    {
                        process.CloseMainWindow();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && RunningApps(list).Any())
            Thread.Sleep(400);
        return RunningApps(list).ToList();
    }

    private static IEnumerable<string> ExistingFolders(CacheTarget target) =>
        target.Folders.Where(f => Directory.Exists(f) && !IsReparsePoint(f));

    private static IEnumerable<string> EnumerateFiles(CacheTarget target, string folder)
    {
        var options = new EnumerationOptions
        {
            // Avec un motif (ex. thumbcache_*.db), seuls les fichiers directement dans le dossier sont visés.
            RecurseSubdirectories = target.FilePattern is null,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // ne jamais suivre un lien vers ailleurs
        };
        try
        {
            return Directory.EnumerateFiles(folder, target.FilePattern ?? "*", options).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool ShouldSkip(CacheTarget target, string folder, string file)
    {
        if (Favorites.IsProtectedPath(file))
            return true;

        if (target.KeepSubfolders.Count > 0)
        {
            var first = Path.GetRelativePath(folder, file).Split('\\', '/')[0];
            if (target.KeepSubfolders.Contains(first, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        if (!target.SkipRecentFiles || !AppSettings.Current.KeepRecentTempFiles)
            return false;
        try
        {
            return File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddHours(-24);
        }
        catch
        {
            return true;
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (IsReparsePoint(dir))
                    continue;
                RemoveEmptyDirectories(dir);
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch
                {
                    // dossier utilisé : on le laisse
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>Garde-fou : un dossier de cache n'est jamais une racine ou un dossier personnel.</summary>
    private static bool IsSafeCacheFolder(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd('\\');
        var forbidden = new[]
        {
            Path.GetPathRoot(full) ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Local, Roaming, ProgramData, WindowsDir,
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        return full.Length > 6 && !forbidden.Any(f => f.Length > 0 && full.Equals(f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static long SafeLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch
        {
            return 0;
        }
    }

    // ----- Nettoyages Windows particuliers -----

    /// <summary>Vide le cache de l'Optimisation de livraison avec la commande prévue par Windows.</summary>
    private static (long Freed, int Skipped) CleanDeliveryOptimization(CacheTarget target)
    {
        var before = Measure(target);
        var (exit, output) = RunPowerShell("Delete-DeliveryOptimizationCache -Force -ErrorAction Stop", 300_000);
        if (exit != 0)
        {
            // Commande absente ou refusée : on vide les dossiers à la main, fichiers utilisés ignorés.
            Journal.Write($"Delete-DeliveryOptimizationCache a échoué (code {exit}) : {output.Trim()}");
            return CleanFolders(target);
        }
        var after = Measure(target);
        var freed = Math.Max(0, before - after);
        Journal.Write($"Cache de l'Optimisation de livraison vidé : {freed} octets libérés.");
        return (freed, 0);
    }

    /// <summary>
    /// Supprime Windows.old avec l'outil de nettoyage de disque de Windows (élément « Installations précédentes »),
    /// qui sait reprendre la propriété des fichiers système. Rien d'autre n'est coché dans ce profil cleanmgr.
    /// </summary>
    private static (long Freed, int Skipped) CleanWindowsOld(CacheTarget target)
    {
        var folder = target.Folders.FirstOrDefault();
        if (folder is null || !Directory.Exists(folder))
            return (0, 0);

        var cleanmgr = Path.Combine(Environment.SystemDirectory, "cleanmgr.exe");
        if (!File.Exists(cleanmgr))
            throw new InvalidOperationException(L.T("L'outil de nettoyage de disque de Windows est introuvable. Supprimez Windows.old depuis Paramètres > Système > Stockage > Fichiers temporaires."));

        var before = Measure(target);
        if (!SetSageFlags(enable: true))
            throw new InvalidOperationException(L.T("Impossible de préparer l'outil de nettoyage de disque de Windows."));
        try
        {
            using var process = Process.Start(new ProcessStartInfo(cleanmgr, $"/sagerun:{SageProfile}") { UseShellExecute = false });
            if (process is null)
                throw new InvalidOperationException(L.T("L'outil de nettoyage de disque de Windows n'a pas pu être lancé."));
            if (!process.WaitForExit((int)TimeSpan.FromMinutes(90).TotalMilliseconds))
                Journal.Write("cleanmgr n'a pas terminé dans le délai : Windows.old est peut-être encore en cours de suppression.");
        }
        finally
        {
            SetSageFlags(enable: false);
        }

        var after = Directory.Exists(folder) ? Measure(target) : 0;
        var freed = Math.Max(0, before - after);
        Journal.Write($"Windows.old : {freed} octets libérés par cleanmgr, {after} octets restants.");
        if (after > 0 && freed == 0)
            throw new InvalidOperationException(L.T("Windows.old n'a pas pu être supprimé. Essayez depuis Paramètres > Système > Stockage > Fichiers temporaires."));
        return (freed, 0);
    }

    /// <summary>
    /// Coche uniquement « Installations précédentes de Windows » dans le profil cleanmgr réservé (ou retire ce profil).
    /// </summary>
    private static bool SetSageFlags(bool enable)
    {
        var valueName = $"StateFlags{SageProfile:D4}";
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var caches = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches");
            if (caches is null)
                return false;
            var found = false;
            foreach (var name in caches.GetSubKeyNames())
            {
                try
                {
                    using var key = caches.OpenSubKey(name, writable: true);
                    if (key is null)
                        continue;
                    if (enable && name.Equals("Previous Installations", StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue(valueName, 2, RegistryValueKind.DWord);
                        found = true;
                    }
                    else
                    {
                        key.DeleteValue(valueName, throwOnMissingValue: false);
                    }
                }
                catch (Exception ex)
                {
                    Journal.Write($"Réglage cleanmgr impossible pour « {name} » : {ex.Message}");
                    // On ne lance jamais cleanmgr si un autre élément est peut-être coché dans ce profil.
                    if (enable && !name.Equals("Previous Installations", StringComparison.OrdinalIgnoreCase) && HasSageValue(caches, name, valueName))
                        return false;
                }
            }
            return !enable || found;
        }
        catch (Exception ex)
        {
            Journal.Write("Réglage cleanmgr impossible : " + ex.Message);
            return false;
        }
    }

    private static bool HasSageValue(RegistryKey caches, string name, string valueName)
    {
        try
        {
            using var key = caches.OpenSubKey(name);
            return key?.GetValue(valueName) is not null;
        }
        catch
        {
            return true; // illisible : prudence
        }
    }

    /// <summary>Nombre de fichiers et taille du cache de l'Optimisation de livraison, selon Windows.</summary>
    private static (int Count, long Bytes) DeliveryOptimizationStatus()
    {
        var (exit, output) = RunPowerShell(
            "$a = @(Get-DeliveryOptimizationStatus -ErrorAction Stop); " +
            "$s = ($a | Measure-Object -Property FileSizeInCache -Sum -ErrorAction SilentlyContinue).Sum; " +
            "Write-Output (\"{0};{1}\" -f $a.Count, [int64]$s)", 60_000);
        if (exit != 0)
            return (0, 0);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        var parts = line.Split(';');
        return parts.Length == 2 && int.TryParse(parts[0], out var count) && long.TryParse(parts[1], out var bytes)
            ? (count, bytes)
            : (0, 0);
    }

    /// <summary>Arrête les services en cours d'exécution demandés par la cible. Renvoie ceux à redémarrer.</summary>
    private static List<string> StopServicesFor(CacheTarget target)
    {
        var names = target.StopServices.Where(n => n.All(char.IsLetterOrDigit)).ToList();
        if (names.Count == 0)
            return new List<string>();

        var list = string.Join(",", names.Select(n => $"'{n}'"));
        // Un service est noté avant l'arrêt : même si l'arrêt échoue à moitié, il sera redémarré.
        var (_, output) = RunPowerShell(
            $"$r = @(); foreach ($n in @({list})) {{ $s = Get-Service -Name $n -ErrorAction SilentlyContinue; " +
            "if ($s -and $s.Status -ne 'Stopped') { $r += $n; Stop-Service -Name $n -Force -ErrorAction SilentlyContinue } }; " +
            "Write-Output ('STOPPED:' + ($r -join ','))", 120_000);
        var line = output.Split('\n', StringSplitOptions.TrimEntries).LastOrDefault(l => l.StartsWith("STOPPED:", StringComparison.Ordinal));
        // Sortie illisible : on redémarre par précaution tous les services demandés.
        var stopped = line is null
            ? names
            : line["STOPPED:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).Where(names.Contains).ToList();
        if (stopped.Count > 0)
            Journal.Write("Services arrêtés pour le nettoyage : " + string.Join(", ", stopped));
        return stopped;
    }

    private static void RestartServices(List<string> names)
    {
        if (names.Count == 0)
            return;
        try
        {
            var list = string.Join(",", names.Select(n => $"'{n}'"));
            var (exit, output) = RunPowerShell($"foreach ($n in @({list})) {{ Start-Service -Name $n -ErrorAction SilentlyContinue }}", 120_000);
            Journal.Write($"Services redémarrés (code {exit}) : {string.Join(", ", names)} {output.Trim()}");
        }
        catch (Exception ex)
        {
            Journal.Write("Redémarrage des services impossible : " + ex.Message);
        }
    }

    /// <summary>Lance un script PowerShell caché et renvoie son code de sortie et sa sortie (-1 en cas d'échec).</summary>
    private static (int ExitCode, string Output) RunPowerShell(string script, int timeoutMs)
    {
        try
        {
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return (-1, string.Empty);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                Journal.Write("PowerShell n'a pas répondu à temps.");
                return (-1, string.Empty);
            }
            process.WaitForExit();
            return (process.ExitCode, process.ExitCode == 0 ? output.Result : error.Result + output.Result);
        }
        catch (Exception ex)
        {
            Journal.Write("PowerShell n'a pas pu être lancé : " + ex.Message);
            return (-1, string.Empty);
        }
    }

    // ----- Corbeille -----

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    private static long RecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }
}
