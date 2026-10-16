using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>Montre précisément ce que le nettoyage va supprimer, et les applications à fermer.</summary>
public partial class PreviewWindow : Window
{
    private readonly List<CacheTarget> _targets;

    public PreviewWindow(List<CacheTarget> targets)
    {
        InitializeComponent();
        _targets = targets;
        Loaded += async (_, _) => await AnalyzeAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private async Task AnalyzeAsync()
    {
        var previews = await Task.Run(() => _targets.AsParallel().AsOrdered().Select(CacheCleaner.Preview).ToList());

        LoadingPanel.Visibility = Visibility.Collapsed;
        ListScroll.Visibility = Visibility.Visible;
        GroupsList.ItemsSource = previews;

        var total = previews.Sum(p => p.TotalBytes);
        Anim.CountBytes(TotalText, total);
        var files = previews.Sum(p => p.TotalFiles);
        SubtitleText.Text = files == 0
            ? "Rien à supprimer pour le moment : ces caches sont déjà vides."
            : $"{files} fichier(s) seront supprimés définitivement. Ils seront recréés si besoin ; vos jeux, comptes et réglages ne sont pas touchés.";
        CleanButton.IsEnabled = files > 0;
        await RefreshRunningAsync();
    }

    private async Task RefreshRunningAsync()
    {
        var running = await Task.Run(() => CacheCleaner.RunningApps(_targets).ToList());
        RunningCard.Visibility = running.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunningText.Text = $"{string.Join(", ", running)} {(running.Count > 1 ? "sont ouverts" : "est ouvert")}. " +
                           "Fermez-les pour un nettoyage complet ; sinon, les fichiers utilisés seront simplement ignorés.";
        if (running.Count > 0)
            Anim.FadeSlideIn(RunningCard, 8);
    }

    private async void CloseApps_Click(object sender, RoutedEventArgs e)
    {
        CloseAppsButton.IsEnabled = false;
        RunningText.Text = "Fermeture en cours…";
        var still = await Task.Run(() => CacheCleaner.CloseApps(_targets));
        CloseAppsButton.IsEnabled = true;
        if (still.Count == 0)
        {
            RunningCard.Visibility = Visibility.Collapsed;
            return;
        }
        RunningText.Text = $"{string.Join(", ", still)} ne s'est pas fermé (une fenêtre demande peut-être une confirmation). " +
                           "Fermez-le vous-même, ou continuez : les fichiers utilisés seront ignorés.";
    }

    private void Entry_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PreviewEntry entry } || !entry.Path.Contains('\\'))
            return;
        try
        {
            var arguments = File.Exists(entry.Path) || Directory.Exists(entry.Path)
                ? $"/select,\"{entry.Path}\""
                : $"\"{Path.GetDirectoryName(entry.Path)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Journal.Write("Ouverture de l'emplacement impossible : " + ex.Message);
        }
    }

    private void Clean_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
