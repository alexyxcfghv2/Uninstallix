using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;

namespace Desinstalleur.App.Views;

/// <summary>
/// Page « Applis Windows » : applications préinstallées (Candy Crush, Xbox, Cortana…) supprimables pour l'utilisateur actuel.
/// Rien n'est coché d'office ; la suppression (Remove-AppxPackage, sans -AllUsers) se fait après confirmation, une par une.
/// </summary>
public partial class WindowsAppsView : UserControl
{
    private readonly ObservableCollection<WindowsApp> _apps = new();
    private readonly ICollectionView _view;
    private bool _busy;
    private bool _loaded;
    private bool _loading;
    private int _loadGeneration;

    public WindowsAppsView()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_apps);
        _view.Filter = Matches;
        AppsList.ItemsSource = _view;

        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible && !_loaded && !_busy)
                await ReloadAsync();
        };
    }

    private Window Host => Window.GetWindow(this) ?? Application.Current.MainWindow;

    // ----- Lecture -----

    private async Task ReloadAsync()
    {
        var generation = ++_loadGeneration;
        _busy = true;
        _loading = true;
        foreach (var app in _apps)
            app.PropertyChanged -= App_PropertyChanged;
        _apps.Clear();
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;
        CountText.Text = "—";
        SafeCountText.Text = "—";
        UpdateFooter();

        List<WindowsApp> apps;
        try
        {
            apps = await Task.Run(WindowsApps.Load);
        }
        catch (Exception ex)
        {
            Journal.Write("Applis Windows : lecture impossible : " + ex);
            if (generation != _loadGeneration)
                return;
            _busy = false;
            _loading = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            EmptyText.Text = L.F("Impossible de lire les applications Windows : {0}", ex.Message);
            EmptyText.Visibility = Visibility.Visible;
            UpdateFooter();
            return;
        }
        if (generation != _loadGeneration)
            return;

        foreach (var app in apps)
        {
            app.PropertyChanged += App_PropertyChanged;
            _apps.Add(app);
        }
        Journal.Write($"Applis Windows : {apps.Count} applications listées.");
        _loaded = true;
        _busy = false;
        _loading = false;
        LoadingPanel.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = true;
        UpdateCounts();
        UpdateFooter();
        if (apps.Count > 0)
            Anim.FadeSlideIn(AppsList, 10, 280);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy)
            await ReloadAsync();
    }

    // ----- Affichage -----

    private bool Matches(object item)
    {
        var query = SearchBox.Text.Trim();
        return query.Length == 0
               || (item is WindowsApp a && a.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateCounts();
        UpdateFooter();
    }

    private List<WindowsApp> VisibleApps() => _view.Cast<WindowsApp>().ToList();

    private void UpdateCounts()
    {
        var remaining = _apps.Where(a => a.Succeeded != true).ToList();
        CountText.Text = remaining.Count.ToString(L.Culture);
        CountLabel.Text = remaining.Count > 1 ? L.T("applications") : L.T("application");
        SafeCountText.Text = remaining.Count(a => a.Advice == WindowsAppAdvice.Safe).ToString(L.Culture);

        if (_loading)
            return;
        if (_apps.Count == 0)
        {
            EmptyText.Text = L.T("Aucune application Windows supprimable n'a été trouvée pour votre compte.\n\nWindows n'a peut-être renvoyé aucune liste : cliquez sur « Actualiser » pour réessayer.");
            EmptyText.Visibility = Visibility.Visible;
        }
        else if (!_view.Cast<object>().Any())
        {
            EmptyText.Text = L.T("Aucune application ne correspond à la recherche.");
            EmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
        }
    }

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowsApp.IsSelected) && !_busy)
            UpdateFooter();
    }

    private List<WindowsApp> Selection() => _apps.Where(a => a.IsSelected && a.CanSelect).ToList();

    private void UpdateFooter()
    {
        var selected = Selection();
        if (!_busy)
        {
            FooterText.Text = selected.Count switch
            {
                0 => L.T("Aucune application cochée."),
                1 => L.T("1 application cochée."),
                _ => L.F("{0} applications cochées.", selected.Count),
            };
        }
        SelectSafeButton.IsEnabled = !_busy && VisibleApps().Any(a => a.Advice == WindowsAppAdvice.Safe && a.CanSelect && !a.IsSelected);
        SelectNoneButton.IsEnabled = !_busy && selected.Count > 0;
        RemoveButton.IsEnabled = !_busy && selected.Count > 0;
    }

    private void SelectSafe_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in VisibleApps().Where(a => a.Advice == WindowsAppAdvice.Safe && a.CanSelect))
            app.IsSelected = true;
        UpdateFooter();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _apps)
            app.IsSelected = false;
        UpdateFooter();
    }

    // ----- Menu contextuel -----

    private static WindowsApp? AppOf(object sender) => (sender as FrameworkElement)?.DataContext as WindowsApp;

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (AppOf(sender) is not { } app)
            return;
        if (string.IsNullOrWhiteSpace(app.InstallLocation) || !Directory.Exists(app.InstallLocation))
        {
            FooterText.Text = L.T("Le dossier de cette application est introuvable.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{app.InstallLocation}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Journal.Write($"Applis Windows : ouverture de {app.InstallLocation} impossible : {ex.Message}");
            FooterText.Text = L.F("Impossible d'ouvrir l'emplacement : {0}", ex.Message);
        }
    }

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        if (AppOf(sender) is not { } app)
            return;
        try
        {
            Clipboard.SetText(app.Name);
        }
        catch (Exception ex)
        {
            Journal.Write($"Applis Windows : copie du nom impossible : {ex.Message}");
        }
    }

    private void OpenStore_Click(object sender, RoutedEventArgs e)
    {
        if (AppOf(sender) is not { } app)
            return;
        try
        {
            var query = Uri.EscapeDataString(app.DisplayName);
            Process.Start(new ProcessStartInfo($"ms-windows-store://search/?query={query}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Journal.Write($"Applis Windows : ouverture du Store impossible : {ex.Message}");
            FooterText.Text = L.F("Impossible d'ouvrir le Microsoft Store : {0}", ex.Message);
        }
    }

    // ----- Suppression -----

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selection();
        if (selected.Count == 0 || _busy)
            return;

        var many = selected.Count > 1;
        var message = L.F(many ? "Désinstaller {0} applications Windows ?" : "Désinstaller {0} application Windows ?", selected.Count) + "\n\n";
        message += string.Join("\n", selected.Take(10).Select(a => "•  " + a.DisplayName)) + "\n";
        if (selected.Count > 10)
            message += L.F(selected.Count - 10 > 1 ? "… et {0} autres" : "… et {0} autre", selected.Count - 10) + "\n";
        message += "\n" + L.T("Elles seront supprimées pour votre compte seulement. Vous pourrez les réinstaller à tout moment depuis le Microsoft Store.");

        var notSafe = selected.Count(a => a.Advice != WindowsAppAdvice.Safe);
        if (notSafe > 0)
            message += "\n\n" + L.F(notSafe > 1
                ? "Attention : {0} applications cochées ne sont pas marquées « Sans risque ». Lisez leur description avant de continuer."
                : "Attention : {0} application cochée n'est pas marquée « Sans risque ». Lisez sa description avant de continuer.", notSafe);
        if (selected.Any(a => a.Advice == WindowsAppAdvice.Keep))
            message += "\n\n" + L.T("Une application marquée « À garder » est cochée : Windows ou d'autres applications peuvent en avoir besoin.");

        var answer = MessageBox.Show(Host, message, L.T("Confirmer la désinstallation"), MessageBoxButton.YesNo,
            notSafe > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _busy = true;
        RefreshButton.IsEnabled = false;
        SearchBox.IsEnabled = false;
        UpdateFooter();
        RemoveProgress.Maximum = selected.Count;
        RemoveProgress.Value = 0;
        RemoveProgress.Visibility = Visibility.Visible;

        var errors = 0;
        var removed = new List<string>();
        for (var i = 0; i < selected.Count; i++)
        {
            var app = selected[i];
            FooterText.Text = L.F("Désinstallation {0} sur {1} : {2}", i + 1, selected.Count, app.DisplayName);
            app.ResultText = null;

            string? error = null;
            foreach (var fullName in app.PackageFullNames)
            {
                var result = await Task.Run(() => WindowsApps.Remove(fullName));
                if (result is null)
                {
                    Journal.Write($"Applis Windows : {fullName} supprimé pour l'utilisateur actuel.");
                }
                else
                {
                    Journal.Write($"Applis Windows : échec de la suppression de {fullName} : {result}");
                    error ??= result;
                }
            }

            if (error is null)
            {
                app.ResultText = L.T("Désinstallée. Réinstallable depuis le Microsoft Store.");
                app.Succeeded = true;
                removed.Add(app.DisplayName);
            }
            else
            {
                app.ResultText = L.F("Échec : {0}", error);
                app.Succeeded = false;
                errors++;
            }
            RemoveProgress.Value = i + 1;
        }

        CleanupHistory.Add("Applis Windows", selected.Select(a => a.DisplayName), 0, errors);
        Journal.Write($"Applis Windows : {removed.Count} applications supprimées, {errors} en échec.");

        _busy = false;
        RefreshButton.IsEnabled = true;
        SearchBox.IsEnabled = true;
        RemoveProgress.Visibility = Visibility.Collapsed;
        UpdateCounts();
        UpdateFooter();
        FooterText.Text = errors == 0
            ? L.F(removed.Count > 1 ? "{0} applications désinstallées." : "{0} application désinstallée.", removed.Count)
            : L.F((removed.Count > 1, errors > 1) switch
            {
                (true, true) => "{0} applications désinstallées, {1} échecs (voir le détail dans la liste).",
                (true, false) => "{0} applications désinstallées, {1} échec (voir le détail dans la liste).",
                (false, true) => "{0} application désinstallée, {1} échecs (voir le détail dans la liste).",
                _ => "{0} application désinstallée, {1} échec (voir le détail dans la liste).",
            }, removed.Count, errors);
        Anim.FadeSlideIn(FooterText, 8);
    }
}
