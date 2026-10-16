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
            MessageBox.Show("Uninstallix est ouvert. Fermez-le, puis relancez la désinstallation.",
                "Désinstaller Uninstallix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show("Voulez-vous vraiment désinstaller Uninstallix ?", "Désinstaller Uninstallix",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var removeData = MessageBox.Show(
            "Supprimer aussi vos réglages, le journal et les sauvegardes du registre ?\n\n" +
            $"Dossier : {Journal.Folder}\n\nRépondez « Non » pour les garder (conseillé si vous comptez réinstaller).",
            "Désinstaller Uninstallix", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

        try
        {
            SelfInstaller.Uninstall(removeData);
            MessageBox.Show("Uninstallix a été désinstallé.", "Désinstaller Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("La désinstallation a échoué :\n\n" + ex.Message, "Désinstaller Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Journal.Write("Erreur inattendue : " + e.Exception);
        MessageBox.Show(
            "Une erreur inattendue s'est produite :\n\n" + e.Exception.Message +
            "\n\nLe détail est dans le journal : " + Journal.Folder,
            "Uninstallix", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
