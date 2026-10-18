using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;

namespace Desinstalleur.App.Views;

/// <summary>Page « Démarrage » : applications lancées avec Windows, activables / désactivables comme dans le Gestionnaire des tâches.</summary>
public partial class StartupView : UserControl
{
    private readonly ObservableCollection<StartupEntry> _entries = new();
    private readonly ICollectionView _view;
    private int _loadGeneration;

    public StartupView()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.Filter = Matches;
        EntriesList.ItemsSource = _view;

        // Rechargé à chaque ouverture de la page : une application a pu s'ajouter au démarrage entre-temps.
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
                await ReloadAsync();
        };
    }

    private Window Host => Window.GetWindow(this) ?? Application.Current.MainWindow;

    private async Task ReloadAsync()
    {
        var generation = ++_loadGeneration;
        List<StartupEntry> entries;
        try
        {
            entries = await Task.Run(StartupManager.Load);
        }
        catch (Exception ex)
        {
            Journal.Write($"Démarrage : lecture impossible : {ex.Message}");
            MessageBox.Show(Host, L.F("Impossible de lire les applications au démarrage : {0}", ex.Message), "Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (generation != _loadGeneration)
            return;

        _entries.Clear();
        foreach (var entry in entries)
            _entries.Add(entry);
        UpdateCounts();

        // Icônes chargées en arrière-plan : la liste s'affiche tout de suite.
        foreach (var entry in entries)
        {
            if (entry.FileMissing || entry.ExePath is null)
                continue;
            var icon = await Task.Run(() => IconLoader.Load(entry.ExePath));
            if (generation != _loadGeneration)
                return;
            entry.Icon = icon;
        }
    }

    private void UpdateCounts()
    {
        var enabled = _entries.Count(e => e.IsEnabled);
        EnabledCountText.Text = enabled.ToString(L.Culture);
        DisabledCountText.Text = (_entries.Count - enabled).ToString(L.Culture);

        var visible = _view.Cast<object>().Any();
        EmptyText.Text = _entries.Count == 0
            ? L.T("Aucune application ne se lance avec Windows.")
            : L.T("Aucune application ne correspond à la recherche.");
        EmptyText.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool Matches(object item)
    {
        var query = SearchBox.Text.Trim();
        return query.Length == 0
               || (item is StartupEntry e && e.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateCounts();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupEntry entry })
            return;

        // La liaison a déjà reporté le nouvel état dans l'entrée : on l'applique au registre, ou on revient en arrière.
        var wanted = entry.IsEnabled;
        try
        {
            StartupManager.SetEnabled(entry, wanted);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            entry.IsEnabled = !wanted;
            Journal.Write($"Démarrage : impossible de modifier « {entry.DisplayName} » : {ex.Message}");
            MessageBox.Show(Host,
                L.F("Windows a refusé de modifier « {0} ».\n\nCette entrée concerne peut-être tous les utilisateurs : relancez Uninstallix en tant qu'administrateur, puis réessayez.", entry.DisplayName),
                "Uninstallix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            entry.IsEnabled = !wanted;
            Journal.Write($"Démarrage : impossible de modifier « {entry.DisplayName} » : {ex.Message}");
            MessageBox.Show(Host, L.F("Impossible de modifier « {0} » : {1}", entry.DisplayName, ex.Message),
                "Uninstallix", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateCounts();
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not StartupEntry entry)
            return;

        // Programme présent : on le sélectionne dans l'Explorateur ; sinon le raccourci du dossier Démarrage.
        var target = !entry.FileMissing && entry.ExePath is not null && File.Exists(entry.ExePath) ? entry.ExePath
                   : entry.FilePath is not null && File.Exists(entry.FilePath) ? entry.FilePath
                   : null;
        if (target is null)
        {
            MessageBox.Show(Host,
                L.T("Le programme lancé par cette entrée est introuvable : il a sans doute été désinstallé ou déplacé.\n\nVous pouvez désactiver cette entrée sans risque."),
                "Uninstallix", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Journal.Write($"Démarrage : ouverture de l'emplacement {target} impossible : {ex.Message}");
            MessageBox.Show(Host, L.F("Impossible d'ouvrir l'emplacement : {0}", ex.Message), "Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not StartupEntry entry)
            return;
        try
        {
            Clipboard.SetText(entry.Command);
        }
        catch (Exception ex)
        {
            Journal.Write($"Démarrage : copie de la commande impossible : {ex.Message}");
        }
    }
}
