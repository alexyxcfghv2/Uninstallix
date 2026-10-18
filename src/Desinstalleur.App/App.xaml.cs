using System.Windows;
using System.Windows.Threading;
using Desinstalleur.App.Services;

namespace Desinstalleur.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);
        L.Init(AppSettings.Current.Language);

        if (SelfInstaller.IsUninstallMode(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RunSelfUninstall();
            Shutdown();
            return;
        }

        if (PortableMode.FallbackReason is { } reason)
            Journal.Write("Mode portable : dossier de données non accessible en écriture, données gardées dans %LocalAppData% : " + reason);
        if (PortableMode.IsActive)
            PortableMode.DeleteLeftoverOldExe();

        if (CleanupReminder.IsReminderMode(e.Args))
        {
            RunReminder(e.Args);
            return;
        }

        // Le même exe sert d'installateur quand il s'appelle « Uninstallix-Setup.exe »,
        // sauf s'il a été relancé en mode portable (« Utiliser sans installer »).
        var setup = SelfInstaller.IsSetupMode(e.Args) &&
                    !(PortableMode.IsActive && !e.Args.Any(a => a.Equals("--installer", StringComparison.OrdinalIgnoreCase)));
        if (setup)
        {
            MainWindow = new Views.SetupWindow();
            MainWindow.Show();
            return;
        }

        ShowMainWindow(PageArgument(e.Args));
    }

    private void ShowMainWindow(string? page)
    {
        Journal.Write(PortableMode.IsActive ? "Démarrage d'Uninstallix (mode portable)." : "Démarrage d'Uninstallix.");
        var window = new Views.MainWindow();
        if (page is not null)
            window.Loaded += (_, _) => window.Navigate(page);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        CleanupReminder.EnsureUpToDate();
    }

    /// <summary>« --page cleanup » : ouvre directement une page de la fenêtre principale.</summary>
    private static string? PageArgument(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// « --rappel » (lancé par la tâche planifiée) : pas de fenêtre principale ; on estime la place récupérable
    /// et on ne prévient que si elle dépasse le seuil. Rien n'est nettoyé automatiquement.
    /// </summary>
    private async void RunReminder(string[] args)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (!AppSettings.Current.CleanupReminder)
            {
                Journal.Write("Rappel de nettoyage lancé alors qu'il est désactivé : rien à faire.");
                Shutdown();
                return;
            }
            var self = Environment.ProcessId;
            var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            if (System.Diagnostics.Process.GetProcessesByName(name).Any(p => p.Id != self))
            {
                Journal.Write("Rappel de nettoyage : Uninstallix est déjà ouvert, pas de rappel.");
                Shutdown();
                return;
            }

            var reclaimable = await Task.Run(CleanupReminder.CheckNeeded);
            if (reclaimable is not { } bytes)
            {
                Shutdown();
                return;
            }

            var reminder = new Views.ReminderWindow(bytes);
            reminder.Closed += (_, _) =>
            {
                if (reminder.OpenCleanup)
                    ShowMainWindow("cleanup");
                else
                    Shutdown();
            };
            reminder.Show();
        }
        catch (Exception ex)
        {
            Journal.Write("Rappel de nettoyage impossible : " + ex);
            Shutdown();
        }
    }

    private static void RunSelfUninstall()
    {
        var self = Environment.ProcessId;
        if (System.Diagnostics.Process.GetProcessesByName("Uninstallix").Any(p => p.Id != self))
        {
            MessageBox.Show(L.T("Uninstallix est ouvert. Fermez-le, puis relancez la désinstallation."),
                L.T("Désinstaller Uninstallix"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(L.T("Voulez-vous vraiment désinstaller Uninstallix ?"), L.T("Désinstaller Uninstallix"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var removeData = MessageBox.Show(
            L.F("Supprimer aussi vos réglages, le journal et les sauvegardes du registre ?\n\n" +
            "Dossier : {0}\n\nRépondez « Non » pour les garder (conseillé si vous comptez réinstaller).", Journal.Folder),
            L.T("Désinstaller Uninstallix"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

        try
        {
            SelfInstaller.Uninstall(removeData);
            MessageBox.Show(L.T("Uninstallix a été désinstallé."), L.T("Désinstaller Uninstallix"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.F("La désinstallation a échoué :\n\n{0}", ex.Message), L.T("Désinstaller Uninstallix"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Journal.Write("Erreur inattendue : " + e.Exception);
        MessageBox.Show(
            L.F("Une erreur inattendue s'est produite :\n\n{0}\n\nLe détail est dans le journal : {1}",
                e.Exception.Message, Journal.Folder),
            "Uninstallix", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
