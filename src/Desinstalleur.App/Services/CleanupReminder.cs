using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Desinstalleur.App.Services;

/// <summary>
/// Rappel de nettoyage : une tâche planifiée Windows lance « Uninstallix --rappel » chaque semaine ou chaque mois.
/// Uninstallix estime alors la place récupérable dans les caches et, seulement si elle dépasse le seuil choisi,
/// affiche une petite fenêtre. Rien n'est jamais nettoyé automatiquement : l'utilisateur décide.
/// </summary>
public static class CleanupReminder
{
    public const string Argument = "--rappel";
    public const string TaskName = @"Uninstallix\Rappel de nettoyage";
    public const string Weekly = "semaine";
    public const string Monthly = "mois";

    public static bool IsReminderMode(string[] args) =>
        args.Any(a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));

    private static string Arguments => PortableMode.FromArgument ? $"{Argument} {PortableMode.Argument}" : Argument;

    private static string Signature(string exe, string frequency) => $"\"{exe}\" {Arguments} | {frequency}";

    /// <summary>Durée entre deux rappels, pour ne pas prévenir juste après un nettoyage.</summary>
    public static TimeSpan Interval(string frequency) =>
        frequency == Monthly ? TimeSpan.FromDays(30) : TimeSpan.FromDays(7);

    /// <summary>Crée ou met à jour la tâche planifiée pour l'exe donné (l'exe en cours par défaut). Renvoie l'erreur, ou null.</summary>
    public static string? Register(string? exe = null)
    {
        exe ??= Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return L.T("Chemin de l'exécutable introuvable.");
        var settings = AppSettings.Current;
        var frequency = settings.ReminderFrequency == Monthly ? Monthly : Weekly;
        var xmlFile = Path.Combine(Path.GetTempPath(), $"Uninstallix-rappel-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(xmlFile, BuildTaskXml(exe, frequency), Encoding.Unicode);
            var (code, output) = RunSchtasks($"/Create /F /TN \"{TaskName}\" /XML \"{xmlFile}\"");
            if (code != 0)
            {
                Journal.Write($"Création du rappel de nettoyage impossible ({code}) : {output}");
                return string.IsNullOrWhiteSpace(output) ? L.F("Code d'erreur {0}.", code) : output.Trim();
            }
            settings.ReminderTaskSignature = Signature(exe, frequency);
            settings.Save();
            Journal.Write($"Rappel de nettoyage programmé ({frequency}) : \"{exe}\" {Arguments}");
            return null;
        }
        catch (Exception ex)
        {
            Journal.Write("Création du rappel de nettoyage impossible : " + ex.Message);
            return ex.Message;
        }
        finally
        {
            try
            {
                File.Delete(xmlFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Supprime la tâche planifiée (sans erreur si elle n'existe pas).</summary>
    public static void Unregister()
    {
        try
        {
            var (code, output) = RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
            Journal.Write(code == 0 ? "Rappel de nettoyage supprimé." : $"Rappel de nettoyage absent ou non supprimé ({code}) : {output.Trim()}");
        }
        catch (Exception ex)
        {
            Journal.Write("Suppression du rappel de nettoyage impossible : " + ex.Message);
        }
        AppSettings.Current.ReminderTaskSignature = null;
        AppSettings.Current.Save();
    }

    /// <summary>Au démarrage : si le rappel est actif mais que l'exe a changé de place (ou de mode), on recrée la tâche.</summary>
    public static void EnsureUpToDate()
    {
        var settings = AppSettings.Current;
        if (!settings.CleanupReminder || Environment.ProcessPath is not { } exe)
            return;
        var frequency = settings.ReminderFrequency == Monthly ? Monthly : Weekly;
        if (settings.ReminderTaskSignature == Signature(exe, frequency))
            return;
        Task.Run(() => Register(exe));
    }

    /// <summary>
    /// Faut-il prévenir ? Renvoie la place récupérable estimée si elle dépasse le seuil et qu'aucun nettoyage n'a eu lieu
    /// récemment, sinon null. Lent (parcourt les caches) : à appeler hors du fil de l'interface.
    /// </summary>
    public static long? CheckNeeded()
    {
        var settings = AppSettings.Current;
        var interval = Interval(settings.ReminderFrequency);
        var last = CleanupHistory.Load().Select(h => h.Date).DefaultIfEmpty(DateTime.MinValue).Max();
        if (DateTime.Now - last < interval)
        {
            Journal.Write($"Rappel de nettoyage : dernier nettoyage le {last:yyyy-MM-dd}, pas de rappel.");
            return null;
        }

        var estimate = EstimateReclaimable();
        var threshold = Math.Max(1, settings.ReminderThresholdMb) * 1024L * 1024L;
        Journal.Write($"Rappel de nettoyage : {estimate} octets récupérables estimés (seuil {threshold}).");
        return estimate >= threshold ? estimate : null;
    }

    /// <summary>Taille des caches cochés dans le profil de nettoyage actif (simple mesure, rien n'est supprimé).</summary>
    public static long EstimateReclaimable()
    {
        var settings = AppSettings.Current;
        var profile = CleanupProfiles.All.Contains(settings.ActiveProfile) ? settings.ActiveProfile : CleanupProfiles.Custom;
        var names = CleanupProfiles.Selection(profile).ToHashSet(StringComparer.Ordinal);
        long total = 0;
        foreach (var target in CacheCleaner.BuildCatalog().Where(t => names.Contains(t.Name)))
        {
            try
            {
                total += CacheCleaner.Measure(target);
            }
            catch (Exception ex)
            {
                Journal.Write($"Rappel de nettoyage : mesure de « {target.Name} » impossible : {ex.Message}");
            }
        }
        return total;
    }

    private static string BuildTaskXml(string exe, string frequency)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? WindowsIdentity.GetCurrent().Name;
        // Premier déclenchement demain à 19 h ; s'il est manqué (PC éteint), la tâche démarre dès que possible.
        var start = DateTime.Today.AddDays(1).AddHours(19).ToString("yyyy-MM-ddTHH:mm:ss");
        var schedule = frequency == Monthly
            ? "<ScheduleByMonth><DaysOfMonth><Day>1</Day></DaysOfMonth><Months><January /><February /><March /><April /><May /><June />" +
              "<July /><August /><September /><October /><November /><December /></Months></ScheduleByMonth>"
            : $"<ScheduleByWeek><WeeksInterval>1</WeeksInterval><DaysOfWeek><{DateTime.Today.AddDays(1).DayOfWeek} /></DaysOfWeek></ScheduleByWeek>";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>Uninstallix</Author>
                <Description>{SecurityElement.Escape(L.T("Prévient quand un nettoyage pourrait libérer de la place. Ne nettoie rien automatiquement."))}</Description>
              </RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>{start}</StartBoundary>
                  <Enabled>true</Enabled>
                  {schedule}
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>{SecurityElement.Escape(Arguments)}</Arguments>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe) ?? string.Empty)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("schtasks.exe");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20000))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            return (-1, "schtasks.exe timeout");
        }
        return (process.ExitCode, (stderr.Result + " " + stdout.Result).Trim());
    }
}
