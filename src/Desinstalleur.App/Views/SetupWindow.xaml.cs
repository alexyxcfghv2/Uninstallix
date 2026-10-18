using System.Diagnostics;
using System.Windows;
using Desinstalleur.App.Services;
using Microsoft.Win32;

namespace Desinstalleur.App.Views;

/// <summary>Assistant d'installation : affiché quand l'exe s'appelle « Uninstallix-Setup.exe ».</summary>
public partial class SetupWindow : Window
{
    private string? _installedExe;
    private bool _installing;

    public SetupWindow()
    {
        InitializeComponent();
        VersionLabel.Text = L.F("Version {0}  ·  Désinstalleur et nettoyeur pour Windows", SelfInstaller.Version);

        var existing = SelfInstaller.ExistingInstallFolder();
        FolderBox.Text = existing ?? SelfInstaller.DefaultFolder;
        if (existing is not null)
        {
            TitleLabel.Text = L.T("Mettre à jour Uninstallix");
            InstallButtonText.Text = L.T("Mettre à jour");
            UpdateNote.Visibility = Visibility.Visible;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Anim.FadeSlideIn(Header, 12, 420);
        Anim.FadeSlideIn(OptionsStep, 18, 450, 140);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Choisissez le dossier où installer Uninstallix") };
        if (dialog.ShowDialog(this) != true)
            return;
        // Comme les installateurs classiques : on crée un sous-dossier « Uninstallix » dans le dossier choisi.
        var chosen = dialog.FolderName;
        FolderBox.Text = Path.GetFileName(chosen.TrimEnd('\\')).Equals("Uninstallix", StringComparison.OrdinalIgnoreCase)
            ? chosen
            : Path.Combine(chosen, "Uninstallix");
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim().Trim('"');
        if (!IsValidFolder(folder, out var problem))
        {
            MessageBox.Show(this, problem, L.T("Dossier d'installation"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var desktop = DesktopBox.IsChecked == true;
        var startMenu = StartMenuBox.IsChecked == true;
        OptionsStep.Visibility = Visibility.Collapsed;
        ProgressStep.Visibility = Visibility.Visible;
        Anim.FadeSlideIn(ProgressStep);
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        PortableButton.Visibility = Visibility.Collapsed;
        _installing = true;

        try
        {
            _installedExe = await Task.Run(() => SelfInstaller.Install(folder, desktop, startMenu,
                text => Dispatcher.Invoke(() => ProgressText.Text = text)));
        }
        catch (Exception ex)
        {
            Journal.Write("Échec de l'installation : " + ex);
            MessageBox.Show(this, L.F("L'installation a échoué :\n\n{0}", ex.Message), "Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ProgressStep.Visibility = Visibility.Collapsed;
            OptionsStep.Visibility = Visibility.Visible;
            Anim.FadeSlideIn(OptionsStep);
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            PortableButton.Visibility = Visibility.Visible;
            return;
        }
        finally
        {
            _installing = false;
        }

        ProgressStep.Visibility = Visibility.Collapsed;
        DoneStep.Visibility = Visibility.Visible;
        Anim.FadeSlideIn(DoneStep);
        Anim.Pop(DoneCheck, 150);
        DoneText.Text = L.F("Installé dans {0}.", folder) + (desktop && startMenu
            ? " " + L.T("Vous le retrouverez sur le Bureau et dans le menu Démarrer.")
            : desktop ? " " + L.T("Vous le retrouverez sur le Bureau.")
            : startMenu ? " " + L.T("Vous le retrouverez dans le menu Démarrer.")
            : string.Empty);
        InstallButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Visible;
    }

    private static bool IsValidFolder(string folder, out string problem)
    {
        problem = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            {
                problem = L.F("Indiquez un chemin complet, par exemple {0}.", @"C:\Program Files\Uninstallix");
                return false;
            }
            var full = Path.GetFullPath(folder).TrimEnd('\\');
            var root = Path.GetPathRoot(full)?.TrimEnd('\\');
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            {
                problem = L.T("Choisissez un dossier dédié (pas la racine d'un disque ni le dossier Windows).");
                return false;
            }
            if (!Directory.Exists(root + "\\"))
            {
                problem = L.T("Ce disque n'existe pas.");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = L.T("Ce chemin n'est pas valide.");
            return false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Essai sans installation : relance ce même fichier en mode portable, rien n'est copié ni inscrit dans Windows.</summary>
    private void Portable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException(L.T("Chemin de l'exécutable introuvable."));
            Process.Start(new ProcessStartInfo(exe, PortableMode.Argument)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            });
            Journal.Write("Lancement en mode portable depuis l'installateur : " + exe);
            Close();
        }
        catch (Exception ex)
        {
            Journal.Write("Lancement en mode portable impossible : " + ex.Message);
            MessageBox.Show(this, L.F("Impossible de lancer le mode portable :\n\n{0}", ex.Message), "Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (LaunchBox.IsChecked == true && _installedExe is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_installedExe) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Journal.Write("Lancement après installation impossible : " + ex.Message);
            }
        }
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_installing)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
