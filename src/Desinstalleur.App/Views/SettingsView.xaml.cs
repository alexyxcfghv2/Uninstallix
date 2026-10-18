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

        _loadingLanguage = true;
        (AppSettings.Current.Language switch { "fr" => LanguageFrench, "en" => LanguageEnglish, _ => LanguageAuto }).IsChecked = true;
        _loadingLanguage = false;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                RefreshFavorites();
        };

        UpdateStatusText.Text = $"Uninstallix {SelfInstaller.Version}";
        if (PortableMode.IsActive)
            UpdateStatusTitle.Text = L.T("Version portable");

        LoadReminder();

        AboutText.Text = L.F("Version {0}.{1}.{2}  ·  Désinstalleur et nettoyeur pour Windows.", version?.Major, version?.Minor, version?.Build) + "\n" +
                         (PortableMode.IsActive ? L.T("Mode portable : Uninstallix n'est pas installé sur ce PC.") + "\n" : string.Empty) +
                         L.F("Journal, réglages et sauvegardes : {0}", Journal.Folder) + "\n" +
                         L.T("© 2026 alexy. Tous droits réservés. Logiciel propriétaire : copie, modification et redistribution interdites sans autorisation.");
    }

    private bool _loadingLanguage;

    private void Setting_Changed(object sender, RoutedEventArgs e) => AppSettings.Current.Save();

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingLanguage || sender is not RadioButton { Tag: string language } || language == AppSettings.Current.Language)
            return;
        AppSettings.Current.Language = language;
        AppSettings.Current.Save();

        if (MessageBox.Show(L.T("La nouvelle langue s'appliquera au prochain démarrage d'Uninstallix. Redémarrer maintenant ?"),
                L.T("Langue"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Journal.Write("Redémarrage impossible : " + ex.Message);
        }
    }

    // ----- Rappel de nettoyage -----

    private bool _loadingReminder;

    private void LoadReminder()
    {
        _loadingReminder = true;
        var settings = AppSettings.Current;
        ReminderToggle.IsChecked = settings.CleanupReminder;
        (settings.ReminderFrequency == CleanupReminder.Monthly ? ReminderMonthly : ReminderWeekly).IsChecked = true;
        (settings.ReminderThresholdMb switch { <= 500 => Threshold500, >= 5120 => Threshold5G, _ => Threshold1G }).IsChecked = true;
        _loadingReminder = false;
        RefreshReminderRows();
    }

    private void RefreshReminderRows()
    {
        var on = AppSettings.Current.CleanupReminder;
        ReminderFrequencyRow.IsEnabled = on;
        ReminderThresholdRow.IsEnabled = on;
        ReminderFrequencyRow.Opacity = on ? 1 : 0.5;
        ReminderThresholdRow.Opacity = on ? 1 : 0.5;
    }

    private void ShowReminderStatus(string text)
    {
        ReminderStatusText.Text = text;
        ReminderStatusText.Visibility = Visibility.Visible;
        Anim.FadeSlideIn(ReminderStatusText, 6);
    }

    private async void Reminder_Toggled(object sender, RoutedEventArgs e)
    {
        var on = ReminderToggle.IsChecked == true;
        AppSettings.Current.CleanupReminder = on;
        AppSettings.Current.Save();
        RefreshReminderRows();
        ReminderToggle.IsEnabled = false;
        try
        {
            if (on)
            {
                var error = await Task.Run(() => CleanupReminder.Register());
                if (error is null)
                {
                    ShowReminderStatus(ReminderSummary());
                }
                else
                {
                    AppSettings.Current.CleanupReminder = false;
                    AppSettings.Current.Save();
                    ReminderToggle.IsChecked = false;
                    RefreshReminderRows();
                    ShowReminderStatus(L.F("Impossible de programmer le rappel : {0}", error));
                }
            }
            else
            {
                await Task.Run(CleanupReminder.Unregister);
                ShowReminderStatus(L.T("Rappel désactivé : la tâche planifiée a été supprimée."));
            }
        }
        finally
        {
            ReminderToggle.IsEnabled = true;
        }
    }

    private async void ReminderFrequency_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingReminder || sender is not RadioButton { Tag: string frequency } || frequency == AppSettings.Current.ReminderFrequency)
            return;
        AppSettings.Current.ReminderFrequency = frequency;
        AppSettings.Current.Save();
        if (!AppSettings.Current.CleanupReminder)
            return;
        var error = await Task.Run(() => CleanupReminder.Register());
        ShowReminderStatus(error is null ? ReminderSummary() : L.F("Impossible de programmer le rappel : {0}", error));
    }

    private void ReminderThreshold_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingReminder || sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var mb))
            return;
        // Le seuil est lu au moment du rappel : pas besoin de recréer la tâche.
        AppSettings.Current.ReminderThresholdMb = mb;
        AppSettings.Current.Save();
        if (AppSettings.Current.CleanupReminder)
            ShowReminderStatus(ReminderSummary());
    }

    private static string ReminderSummary()
    {
        var threshold = Desinstalleur.Core.ValueParsers.FormatSize(AppSettings.Current.ReminderThresholdMb * 1024L * 1024L);
        return AppSettings.Current.ReminderFrequency == CleanupReminder.Monthly
            ? L.F("✓ Rappel programmé : chaque mois, si au moins {0} peuvent être libérés.", threshold)
            : L.F("✓ Rappel programmé : chaque semaine, si au moins {0} peuvent être libérés.", threshold);
    }

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
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Dossier à ne jamais nettoyer") };
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
            ToolResultText.Text = L.F("« {0} » n'est plus protégé. Actualisez la liste des logiciels pour le voir.", program);
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = L.T("Recherche d'une nouvelle version…");
        try
        {
            var update = await UpdateChecker.CheckAsync();
            if (update is null)
            {
                UpdateStatusText.Text = L.F("✓ Vous avez la dernière version ({0}).", SelfInstaller.Version);
            }
            else
            {
                UpdateStatusText.Text = L.F("Version {0} disponible.", update.Version.ToString(3));
                new UpdateWindow(update) { Owner = Window.GetWindow(this) }.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = L.F("Impossible de joindre GitHub : {0}", ex.Message);
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
        ToolResultText.Text = L.T("Création du point de restauration… (cela peut prendre une minute)");
        var error = await Task.Run(() => RestorePoint.Create(L.T("Uninstallix - point manuel")));
        ToolResultText.Text = error is null
            ? L.T("✓ Point de restauration demandé. Windows n'en crée qu'un toutes les 24 h : s'il en existe déjà un récent, il est conservé.")
            : L.F("Impossible de créer le point de restauration : {0}", error);
        Anim.FadeSlideIn(ToolResultText, 8);
        RestorePointButton.IsEnabled = true;
    }

    private void OpenJournal_Click(object sender, RoutedEventArgs e)
    {
        var file = Path.Combine(Journal.Folder, "journal.txt");
        if (File.Exists(file))
            Open(file);
        else
            ToolResultText.Text = L.T("Le journal est vide pour le moment.");
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
            ToolResultText.Text = L.F("Impossible d'ouvrir : {0}", ex.Message);
        }
    }
}
