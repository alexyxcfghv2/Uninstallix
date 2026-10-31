using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Desinstalleur.App.Models;

namespace Desinstalleur.App.Services;

/// <summary>
/// Mode jeu : ferme gentiment les applications choisies, libère la RAM, passe en mode « Performances élevées »,
/// puis rouvre tout et rétablit le mode d'alimentation à la désactivation.
/// L'état est enregistré dans « mode-jeu.json » (dossier des données) pour survivre à la fermeture d'Uninstallix.
/// </summary>
public static class GameMode
{
    public static string StateFile => Path.Combine(Journal.Folder, "mode-jeu.json");

    /// <summary>GUID du mode « Performances élevées » (alias SCHEME_MIN de powercfg).</summary>
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ----- État -----

    public static GameModeState LoadState()
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(StateFile))
                {
                    var state = JsonSerializer.Deserialize<GameModeState>(File.ReadAllText(StateFile));
                    if (state is not null)
                    {
                        state.Choices = new Dictionary<string, bool>(state.Choices ?? new(), StringComparer.OrdinalIgnoreCase);
                        state.ClosedApps ??= new();
                        return state;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Journal.Write("Mode jeu : lecture de l'état impossible : " + ex.Message);
        }
        return new GameModeState();
    }

    public static void SaveState(GameModeState state)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Journal.Folder);
                var temp = StateFile + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
                File.Move(temp, StateFile, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Journal.Write("Mode jeu : enregistrement de l'état impossible : " + ex.Message);
        }
    }

    // ----- Listes de processus -----

    /// <summary>Processus de Windows (jamais proposés).</summary>
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
        "fontdrvhost", "dwm", "explorer", "sihost", "taskhostw", "ctfmon", "runtimebroker", "searchhost", "searchapp",
        "searchui", "searchindexer", "searchprotocolhost", "searchfilterhost", "startmenuexperiencehost",
        "shellexperiencehost", "shellhost", "textinputhost", "applicationframehost", "systemsettings", "lockapp",
        "conhost", "dllhost", "smartscreen", "securityhealthsystray", "securityhealthservice", "msmpeng", "nissrv",
        "mpdefendercoreservice", "taskmgr", "audiodg", "spoolsv", "wudfhost", "widgets", "widgetservice",
        "useroobebroker", "crossdeviceresume", "phoneexperiencehost", "gamebarpresencewriter", "backgroundtaskhost",
        "memory compression", "secure system", "wmiprvse", "msiexec", "consent", "logonui", "userinit", "rundll32",
        "windowsterminal", "openconsole", "cmd", "powershell", "pwsh", "mmc", "regedit", "uninstallix",
    };

    /// <summary>Jeux, lanceurs, anti-triche et outils des pilotes (jamais proposés).</summary>
    private static readonly string[] ExcludedNameParts =
    {
        // Lanceurs et plateformes
        "steam", "epicgames", "epicweb", "epiconline", "riotclient", "leagueclient", "league of legends", "valorant",
        "rockstar", "socialclub", "launcherpatcher", "playgtav", "gta5", "fivem", "altv", "ragemp", "battle.net",
        "blizzard", "eadesktop", "eabackground", "eaconnect", "uplay", "ubisoft", "goggalaxy",
        "galaxyclient", "xboxapp", "xboxpcapp", "gamingservices", "gamebar", "minecraft", "playnite",
        "amazon games", "amazongames", "heroic", "bethesda", "wargaming", "hoyoplay", "genshin", "overwolf",
        "curseforge", "faceit", "medal",
        // Anti-triche
        "easyanticheat", "eac_", "beservice", "battleye", "vgtray", "vanguard", "xigncode", "gameguard",
        "nprotect", "ricochet", "punkbuster", "pnkbstr", "mhyprot", "acclient",
        // Pilotes et outils matériels
        "nvcontainer", "nvidia", "nvsphelper", "nvdisplay", "nvbroadcast", "radeonsoftware", "amdrs", "amdow", "amdfendr",
        "atieclxx", "atiesrxx", "igfx", "intelgraphics", "igcc", "msiafterburner", "rtss", "rivatuner", "lghub",
        "logitech", "razer", "rzsynapse", "icue", "corsair", "armourycrate", "asus", "steelseries", "realtek", "rtkaud",
        "nahimic", "dolby", "wacom", "hpprint", "synaptics", "msi center", "dragon center",
    };

    /// <summary>Noms trop courts pour une recherche partielle : comparés en entier.</summary>
    private static readonly HashSet<string> ExcludedExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "upc", "origin", "itch", "wgc", "esea", "esportal", "vgc", "rtss", "agent", "javaw", "elanfp", "etdctrl",
        "gog", "ea", "r5apex", "cs2", "csgo", "dota2", "fortniteclient-win64-shipping", "robloxplayerbeta", "roblox",
    };

    /// <summary>Applications qui tournent souvent sans fenêtre mais que l'on peut vouloir fermer avant de jouer.</summary>
    private static readonly HashSet<string> KnownBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "arc", "discord", "discordptb",
        "discordcanary", "spotify", "teams", "ms-teams", "slack", "skype", "onedrive", "dropbox", "googledrivefs",
        "whatsapp", "telegram", "zoom", "signal", "teamspeak", "ts3client_win64", "mumble", "creative cloud",
        "adobe desktop service", "icloud", "icloudservices", "megasync", "qbittorrent", "utorrent", "bittorrent",
        "obs64", "streamlabs obs", "wallpaper32", "wallpaper64", "lively", "notion", "thunderbird", "outlook",
    };

    /// <summary>Applications vocales et de diffusion : gardées par défaut (on parle pendant qu'on joue).</summary>
    private static readonly HashSet<string> VoiceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "discordptb", "discordcanary", "teamspeak", "ts3client_win64", "ts3client_win32", "mumble",
        "ventrilo", "guilded", "element", "teams", "ms-teams", "skype", "zoom", "signal", "whatsapp", "telegram",
        "obs64", "obs32", "streamlabs obs", "xsplit.core",
    };

    /// <summary>Dossiers de jeux : un programme lancé depuis l'un d'eux n'est jamais proposé.</summary>
    private static readonly string[] ExcludedPathParts =
    {
        @"\steamapps\", @"\steam\", @"\epic games\", @"\riot games\", @"\rockstar games\", @"\fivem", @"\battle.net\",
        @"\ea games\", @"\electronic arts\", @"\ubisoft\", @"\gog galaxy\", @"\gog games\", @"\xboxgames\",
        @"\modifiablewindowsapps\", @"\nvidia corporation\", @"\amd\", @"\intel\", @"\games\",
    };

    private static bool IsExcluded(string name, string? exePath)
    {
        if (SystemNames.Contains(name) || ExcludedExactNames.Contains(name))
            return true;
        var lower = name.ToLowerInvariant();
        if (ExcludedNameParts.Any(lower.Contains))
            return true;
        if (exePath is null)
            return false;
        var path = exePath.ToLowerInvariant();
        if (ExcludedPathParts.Any(path.Contains))
            return true;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\').ToLowerInvariant();
        return windows.Length > 0 && path.StartsWith(windows + "\\", StringComparison.Ordinal);
    }

    /// <summary>Vrai pour une application vocale, conseillée de garder.</summary>
    public static bool IsVoiceApp(string key) => VoiceNames.Contains(key);

    // ----- Lecture des applications ouvertes -----

    /// <summary>Applications de la session de l'utilisateur, regroupées par nom de processus, triées par RAM.</summary>
    public static List<GameModeApp> ListApps(GameModeState state)
    {
        var withWindow = ProcessesWithVisibleWindow();
        var selfId = Environment.ProcessId;
        var session = CurrentSessionId();
        var groups = new Dictionary<string, GameModeApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == selfId || process.Id <= 4 || process.SessionId != session)
                    continue;
                var name = process.ProcessName;
                if (SystemNames.Contains(name))
                    continue;

                if (!groups.TryGetValue(name, out var app))
                {
                    var exe = ExePathOf(process.Id);
                    if (IsExcluded(name, exe))
                    {
                        groups[name] = null!; // marqué exclu : on ne relit pas les autres processus du même nom
                        continue;
                    }
                    var key = name.ToLowerInvariant();
                    app = new GameModeApp
                    {
                        Key = key,
                        ProcessName = name,
                        DisplayName = DisplayNameOf(exe, name),
                        ExePath = exe,
                        IsVoice = VoiceNames.Contains(key),
                    };
                    groups[name] = app;
                }
                if (app is null)
                    continue;

                app.ProcessIds.Add(process.Id);
                if (withWindow.Contains(process.Id))
                    app.HasWindow = true;
                try
                {
                    app.RamBytes += process.WorkingSet64;
                }
                catch
                {
                    // processus terminé entre-temps
                }
            }
            catch
            {
                // processus protégé ou terminé entre-temps
            }
            finally
            {
                process.Dispose();
            }
        }

        var list = groups.Values
            .Where(a => a is not null && a.ExePath is not null && (a.HasWindow || KnownBackground.Contains(a.Key)))
            .OrderByDescending(a => a.RamBytes)
            .ToList();

        foreach (var app in list)
            app.Close = state.Choices.TryGetValue(app.Key, out var close) ? close : false; // rien de coché par défaut
        return list;
    }

    private static string DisplayNameOf(string? exe, string processName)
    {
        try
        {
            if (exe is not null && File.Exists(exe))
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                var description = info.FileDescription?.Trim();
                if (!string.IsNullOrWhiteSpace(description) && description.Length <= 60)
                    return description;
                if (!string.IsNullOrWhiteSpace(info.ProductName))
                    return info.ProductName.Trim();
            }
        }
        catch
        {
            // pas d'informations de version
        }
        return processName;
    }

    private static int CurrentSessionId()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            return self.SessionId;
        }
        catch
        {
            return -1;
        }
    }

    // ----- Fermeture -----

    public sealed record CloseResult(List<GameModeClosedApp> Closed, List<GameModeApp> Refused);

    /// <summary>
    /// Demande gentiment aux applications de se fermer (comme un clic sur la croix), attend, puis renvoie celles qui
    /// se sont fermées et celles qui ont refusé. Ne tue jamais un processus.
    /// </summary>
    public static CloseResult CloseGently(IReadOnlyList<GameModeApp> apps, TimeSpan wait)
    {
        var launchInfo = new Dictionary<GameModeApp, GameModeClosedApp>();
        foreach (var app in apps)
            launchInfo[app] = CaptureLaunchInfo(app);

        foreach (var app in apps)
        {
            var ids = app.ProcessIds.ToHashSet();
            foreach (var hwnd in TopLevelWindows(ids))
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            foreach (var id in ids)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    process.CloseMainWindow();
                }
                catch
                {
                    // déjà fermé
                }
            }
            Journal.Write($"Mode jeu : demande de fermeture envoyée à « {app.DisplayName} ».");
        }

        var deadline = DateTime.UtcNow + wait;
        var pending = apps.ToList();
        while (pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(400);
            pending.RemoveAll(a => !IsRunning(a.ProcessName, a.ProcessIds));
        }

        var closed = apps.Except(pending).Select(a => launchInfo[a]).ToList();
        foreach (var app in pending)
            Journal.Write($"Mode jeu : « {app.DisplayName} » ne s'est pas fermé.");
        return new CloseResult(closed, pending);
    }

    /// <summary>Force la fermeture (après accord de l'utilisateur). Renvoie les applications réellement fermées.</summary>
    public static List<GameModeClosedApp> ForceClose(IReadOnlyList<GameModeApp> apps)
    {
        var closed = new List<GameModeClosedApp>();
        foreach (var app in apps)
        {
            var info = CaptureLaunchInfo(app);
            foreach (var id in app.ProcessIds)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    if (process.ProcessName.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase))
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // déjà fermé, ou refusé par Windows
                }
            }
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (IsRunning(app.ProcessName, app.ProcessIds) && DateTime.UtcNow < deadline)
                Thread.Sleep(200);

            if (IsRunning(app.ProcessName, app.ProcessIds))
            {
                Journal.Write($"Mode jeu : fermeture forcée de « {app.DisplayName} » impossible.");
            }
            else
            {
                Journal.Write($"Mode jeu : « {app.DisplayName} » fermé de force.");
                closed.Add(info);
            }
        }
        return closed;
    }

    private static bool IsRunning(string processName, IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (!process.HasExited && process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // terminé
            }
        }
        return false;
    }

    /// <summary>Retient de quoi relancer l'application : exe, arguments du processus principal et dossier de travail.</summary>
    private static GameModeClosedApp CaptureLaunchInfo(GameModeApp app)
    {
        var info = new GameModeClosedApp
        {
            Key = app.Key,
            DisplayName = app.DisplayName,
            ExePath = app.ExePath ?? string.Empty,
            WorkingDirectory = app.ExePath is null ? null : Path.GetDirectoryName(app.ExePath),
        };

        // Processus principal : celui qui a une fenêtre, sinon le premier sans « --type= » (sous-processus de Chromium).
        var windows = ProcessesWithVisibleWindow();
        var candidates = app.ProcessIds.OrderByDescending(id => windows.Contains(id)).ToList();
        foreach (var id in candidates)
        {
            var commandLine = CommandLineOf(id);
            if (commandLine is null || commandLine.Contains("--type=", StringComparison.OrdinalIgnoreCase))
                continue;
            info.Arguments = StripExecutable(commandLine);
            break;
        }
        return info;
    }

    /// <summary>Retire le premier élément (le programme lui-même) d'une ligne de commande.</summary>
    private static string StripExecutable(string commandLine)
    {
        var s = commandLine.TrimStart();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end < 0 ? string.Empty : s[(end + 1)..].Trim();
        }
        var space = s.IndexOf(' ');
        return space < 0 ? string.Empty : s[(space + 1)..].Trim();
    }

    // ----- Réouverture -----

    /// <summary>
    /// Rouvre une application fermée, si possible sans les droits administrateur d'Uninstallix :
    /// d'abord par le bureau de Windows (avec les arguments), sinon via « explorer.exe "chemin" », en dernier recours directement.
    /// Renvoie null si tout va bien, sinon un message d'erreur.
    /// </summary>
    public static string? Reopen(GameModeClosedApp app)
    {
        if (string.IsNullOrWhiteSpace(app.ExePath) || !File.Exists(app.ExePath))
            return L.T("Le programme est introuvable.");

        if (IsExeRunning(app.ExePath))
        {
            Journal.Write($"Mode jeu : « {app.DisplayName} » est déjà ouvert, rien à rouvrir.");
            return null;
        }

        try
        {
            if (ShellExecuteAsDesktopUser(app.ExePath, app.Arguments, app.WorkingDirectory))
            {
                Journal.Write($"Mode jeu : « {app.DisplayName} » rouvert (utilisateur normal).");
                return null;
            }
        }
        catch (Exception ex)
        {
            Journal.Write($"Mode jeu : réouverture par le bureau impossible pour « {app.DisplayName} » : {ex.Message}");
        }

        try
        {
            // Lancé par l'Explorateur, le programme n'hérite pas des droits administrateur (mais sans ses arguments).
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", "\"" + app.ExePath + "\"") { UseShellExecute = true });
            Journal.Write($"Mode jeu : « {app.DisplayName} » rouvert via l'Explorateur.");
            return null;
        }
        catch (Exception ex)
        {
            Journal.Write($"Mode jeu : réouverture via l'Explorateur impossible pour « {app.DisplayName} » : {ex.Message}");
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(app.ExePath, app.Arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = app.WorkingDirectory ?? string.Empty,
            });
            Journal.Write($"Mode jeu : « {app.DisplayName} » rouvert directement (droits administrateur).");
            return null;
        }
        catch (Exception ex)
        {
            Journal.Write($"Mode jeu : réouverture impossible pour « {app.DisplayName} » : {ex.Message}");
            return ex.Message;
        }
    }

    private static bool IsExeRunning(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (string.Equals(ExePathOf(process.Id), exePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    /// <summary>
    /// Demande au bureau de Windows (explorer.exe, sans droits administrateur) de lancer le programme,
    /// via IShellDispatch2.ShellExecute : le programme démarre comme si l'utilisateur l'avait ouvert lui-même.
    /// </summary>
    private static bool ShellExecuteAsDesktopUser(string file, string arguments, string? directory)
    {
        var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (type is null || Activator.CreateInstance(type) is not IShellWindows windows)
            return false;

        object location = CSIDL_DESKTOP;
        object? root = null;
        var dispatch = windows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
        if (dispatch is not IServiceProvider provider)
            return false;

        var sid = SID_STopLevelBrowser;
        var iid = typeof(IShellBrowser).GUID;
        provider.QueryService(ref sid, ref iid, out var browserObject);
        if (browserObject is not IShellBrowser browser)
            return false;

        browser.QueryActiveShellView(out var view);
        var dispatchId = IID_IDispatch;
        view.GetItemObject(SVGIO_BACKGROUND, ref dispatchId, out var folderView);
        var application = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null);
        if (application is null)
            return false;

        application.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, application,
            new object[] { file, arguments ?? string.Empty, directory ?? string.Empty, "open", 1 });
        return true;
    }

    // ----- Mode d'alimentation -----

    /// <summary>GUID du mode d'alimentation actif, ou null si powercfg n'a pas répondu.</summary>
    public static string? GetActivePowerScheme()
    {
        var output = RunPowerCfg("/getactivescheme", out var exit);
        if (exit != 0 || output is null)
            return null;
        var match = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    /// <summary>Active un mode d'alimentation (GUID ou alias comme SCHEME_MIN). Renvoie vrai si powercfg a réussi.</summary>
    public static bool SetPowerScheme(string scheme)
    {
        if (!Regex.IsMatch(scheme, @"^[A-Za-z0-9_\-]+$"))
            return false;
        RunPowerCfg("/setactive " + scheme, out var exit);
        Journal.Write(exit == 0 ? $"Mode jeu : mode d'alimentation {scheme} activé." : $"Mode jeu : échec de powercfg /setactive {scheme}.");
        return exit == 0;
    }

    private static string? RunPowerCfg(string arguments, out int exitCode)
    {
        exitCode = -1;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15_000))
                return null;
            exitCode = process.ExitCode;
            return output.Result;
        }
        catch (Exception ex)
        {
            Journal.Write("Mode jeu : powercfg impossible : " + ex.Message);
            return null;
        }
    }

    // ----- Win32 -----

    private static HashSet<int> ProcessesWithVisibleWindow()
    {
        var ids = new HashSet<int>();
        EnumWindows((hwnd, _) =>
        {
            if (IsCandidateWindow(hwnd))
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                ids.Add((int)pid);
            }
            return true;
        }, IntPtr.Zero);
        return ids;
    }

    private static List<IntPtr> TopLevelWindows(HashSet<int> processIds)
    {
        var list = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (processIds.Contains((int)pid) && IsCandidateWindow(hwnd))
                list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Fenêtre principale visible : sans propriétaire, avec un titre, pas une fenêtre-outil, pas « masquée » (cloaked).</summary>
    private static bool IsCandidateWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero || GetWindowTextLength(hwnd) == 0)
            return false;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            return false;
        try
        {
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;
        }
        catch
        {
            // DWM indisponible
        }
        return true;
    }

    private static string? ExePathOf(int processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Ligne de commande d'un processus (Windows 8.1 et plus), ou null.</summary>
    private static string? CommandLineOf(int processId)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
            if (needed <= 0 || needed > 1 << 20)
                return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, needed, out _) != 0)
                    return null;
                var text = Marshal.PtrToStructure<UNICODE_STRING>(buffer);
                return text.Buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint WM_CLOSE = 0x0010;
    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const int DWMWA_CLOAKED = 14;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private const int CSIDL_DESKTOP = 0;
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_BACKGROUND = 0;
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returnLength);

    // Interfaces COM du Shell, juste ce qu'il faut pour IShellDispatch2.ShellExecute depuis le bureau.

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
        int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass);
        int RegisterPending(int threadId, ref object loc, ref object locRoot, int swClass);
        void Revoke(int cookie);
        void OnNavigate(int cookie, ref object loc);
        void OnActivated(int cookie, bool active);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object loc, ref object? locRoot, int swClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(bool enterMode);
        void InsertMenusSB(IntPtr shared, IntPtr widths);
        void SetMenuSB(IntPtr shared, IntPtr oleMenu, IntPtr activeObject);
        void RemoveMenusSB(IntPtr shared);
        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        void EnableModelessSB(bool enable);
        void TranslateAcceleratorSB(IntPtr msg, ushort id);
        void BrowseObject(IntPtr pidl, uint flags);
        void GetViewStateStream(uint mode, out IntPtr stream);
        void GetControlWindow(uint id, out IntPtr hwnd);
        void SendControlMsg(uint id, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        void QueryActiveShellView(out IShellView view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(bool enterMode);
        void TranslateAccelerator(IntPtr msg);
        void EnableModeless(bool enable);
        void UIActivate(uint state);
        void Refresh();
        void CreateViewWindow(IntPtr previous, IntPtr settings, IntPtr browser, IntPtr rect, out IntPtr hwnd);
        void DestroyViewWindow();
        void GetCurrentInfo(IntPtr settings);
        void AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr lParam);
        void SaveViewState();
        void SelectItem(IntPtr pidl, uint flags);
        void GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
}
