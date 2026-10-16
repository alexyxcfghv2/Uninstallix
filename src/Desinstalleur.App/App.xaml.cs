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

        // Le même exe sert d'installateur quand il s'appelle « Uninstallix-Setup.exe ».
        Window window = SelfInstaller.IsSetupMode(e.Args) ? new Views.SetupWindow() : new Views.MainWindow();
        if (window is Views.MainWindow)
            Journal.Write("Démarrage d'Uninstallix.");
        MainWindow = window;
        window.Show();
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
