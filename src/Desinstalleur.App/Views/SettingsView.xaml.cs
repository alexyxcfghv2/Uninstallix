using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Desinstalleur.App.Services;

namespace Desinstalleur.App.Views;

/// <summary>Page « Réglages » : options enregistrées dans reglages.json, outils et à propos.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = AppSettings.Current;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                RefreshFavorites();
        };

        UpdateStatusText.Text = $"Uninstallix {SelfInstaller.Version}";

        AboutText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}  ·  Désinstalleur et nettoyeur pour Windows.\n" +
                         $"Journal, réglages et sauvegardes : {Journal.Folder}";
    }

    private void Setting_Changed(object sender, RoutedEventArgs e) => AppSettings.Current.Save();

    // ----- Favoris protégés -----

    private void RefreshFavorites()
    {
        FoldersList.ItemsSource = Favorites.Folders.ToList();
        ProgramsList.ItemsSource = Favorites.Programs.OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToList();
        NoFoldersText.Visibility = Favorites.Folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoProgramsText.Visibility = Favorites.Programs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier à ne jamais nettoyer" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        Favorites.AddFolder(dialog.FolderName);
        RefreshFavorites();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string folder })
        {
            Favorites.RemoveFolder(folder);
            RefreshFavorites();
        }
    }

    private void RemoveProgram_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string program })
        {
            Favorites.RemoveProgram(program);
            RefreshFavorites();
            ToolResultText.Text = $"« {program} » n'est plus protégé. Actualisez la liste des logiciels pour le voir.";
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Recherche d'une nouvelle version…";
        try
        {
            var update = await UpdateChecker.CheckAsync();
            if (update is null)
            {
                UpdateStatusText.Text = $"✓ Vous avez la dernière version ({SelfInstaller.Version}).";
            }
            else
            {
                UpdateStatusText.Text = $"Version {update.Version.ToString(3)} disponible.";
                new UpdateWindow(update) { Owner = Window.GetWindow(this) }.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Impossible de joindre GitHub : " + ex.Message;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            Anim.FadeSlideIn(UpdateStatusText, 6);
        }
    }

    private void ReportProblem_Click(object sender, RoutedEventArgs e) =>
        new ReportWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private async void CreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        RestorePointButton.IsEnabled = false;
        ToolResultText.Text = "Création du point de restauration… (cela peut prendre une minute)";
        var error = await Task.Run(() => RestorePoint.Create("Uninstallix - point manuel"));
        ToolResultText.Text = error is null
            ? "✓ Point de restauration demandé. Windows n'en crée qu'un toutes les 24 h : s'il en existe déjà un récent, il est conservé."
            : "Impossible de créer le point de restauration : " + error;
        Anim.FadeSlideIn(ToolResultText, 8);
        RestorePointButton.IsEnabled = true;
    }

    private void OpenJournal_Click(object sender, RoutedEventArgs e)
    {
        var file = Path.Combine(Journal.Folder, "journal.txt");
        if (File.Exists(file))
            Open(file);
        else
            ToolResultText.Text = "Le journal est vide pour le moment.";
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Journal.RegistryBackupFolder);
        Open(Journal.RegistryBackupFolder);
    }

    private void OpenSystemRestore_Click(object sender, RoutedEventArgs e) =>
        Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rstrui.exe"));

    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ToolResultText.Text = "Impossible d'ouvrir : " + ex.Message;
        }
    }
}
