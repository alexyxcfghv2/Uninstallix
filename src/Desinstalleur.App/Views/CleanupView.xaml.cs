using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>Page « Nettoyage » : caches des applications, RAM et cache DNS.</summary>
public partial class CleanupView : UserControl
{
    private readonly List<CacheTarget> _targets = CacheCleaner.BuildCatalog();
    private readonly DispatcherTimer _ramTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _analyzed;
    private bool _busy;
    private bool _applyingProfile;
    private string _profile = CleanupProfiles.Custom;

    public CleanupView()
    {
        InitializeComponent();

        // Profil retenu la dernière fois.
        _profile = CleanupProfiles.All.Contains(AppSettings.Current.ActiveProfile) ? AppSettings.Current.ActiveProfile : CleanupProfiles.Custom;
        ApplyProfile(_profile);
        (_profile == CleanupProfiles.Gaming ? ProfileGaming : _profile == CleanupProfiles.Browsers ? ProfileBrowsers : ProfileCustom).IsChecked = true;

        TargetsList.ItemsSource = _targets;
        foreach (var target in _targets)
            target.PropertyChanged += Target_PropertyChanged;

        _ramTimer.Tick += (_, _) => UpdateRam();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                UpdateRam();
                _ramTimer.Start();
            }
            else
            {
                _ramTimer.Stop();
            }
        };
        UpdateSelection();
    }

    private Window Host => Window.GetWindow(this) ?? Application.Current.MainWindow;

    /// <summary>Lance l'analyse la première fois que la page est ouverte.</summary>
    public void AnalyzeIfNeeded()
    {
        if (!_analyzed)
            _ = AnalyzeAsync();
    }

    private async Task AnalyzeAsync()
    {
        if (_busy)
            return;
        _busy = true;
        _analyzed = true;
        CleanButton.IsEnabled = false;

        foreach (var target in _targets)
        {
            target.SizeBytes = null;
            target.StatusText = null;
            target.Succeeded = null;
        }

        // Analyse en parallèle : chaque cache est mesuré dans son coin.
        await Task.WhenAll(_targets.Select(target => Task.Run(() =>
        {
            long size;
            try
            {
                size = CacheCleaner.Measure(target);
            }
            catch
            {
                size = 0;
            }
            Dispatcher.Invoke(() => target.SizeBytes = size);
        })));

        _busy = false;
        UpdateSelection();
    }

    // ----- Profils -----

    private void Profile_Checked(object sender, RoutedEventArgs e)
    {
        // Appelé aussi pendant InitializeComponent.
        if (sender is not System.Windows.Controls.RadioButton { Tag: string profile } || TargetsList is null || profile == _profile)
            return;
        _profile = profile;
        ApplyProfile(profile);
        AppSettings.Current.ActiveProfile = profile;
        AppSettings.Current.Save();
    }

    private void ApplyProfile(string profile)
    {
        var names = CleanupProfiles.Selection(profile);
        _applyingProfile = true;
        foreach (var target in _targets)
            target.IsSelected = names.Contains(target.Name, StringComparer.OrdinalIgnoreCase);
        _applyingProfile = false;
        UpdateSelection();
    }

    private void SaveProfile() =>
        CleanupProfiles.SaveSelection(_profile, _targets.Where(t => t.IsSelected).Select(t => t.Name));

    private void Target_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CacheTarget.IsSelected) && !_applyingProfile)
            SaveProfile();
        if (e.PropertyName is nameof(CacheTarget.IsSelected) or nameof(CacheTarget.SizeBytes))
            UpdateSelection();
    }

    private void UpdateSelection()
    {
        var total = _targets.Sum(t => t.SizeBytes ?? 0);
        if (_analyzed)
            Anim.CountBytes(TotalCacheText, total);
        else
            TotalCacheText.Text = "—";

        var selected = _targets.Where(t => t.IsSelected).ToList();
        var size = selected.Sum(t => t.SizeBytes ?? 0);
        SelectionText.Text = selected.Count == 0
            ? L.T("Aucun élément coché")
            : size > 0
                ? L.F(selected.Count > 1 ? "{0} éléments cochés  ·  {1} à libérer" : "{0} élément coché  ·  {1} à libérer", selected.Count, ValueParsers.FormatSize(size))
                : L.F(selected.Count > 1 ? "{0} éléments cochés" : "{0} élément coché", selected.Count);
        CleanButton.IsEnabled = !_busy && selected.Count > 0;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _applyingProfile = true;
        foreach (var target in _targets)
            target.IsSelected = true;
        _applyingProfile = false;
        SaveProfile();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _applyingProfile = true;
        foreach (var target in _targets)
            target.IsSelected = false;
        _applyingProfile = false;
        SaveProfile();
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _targets.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0 || _busy)
            return;

        // Aperçu détaillé (fichiers, tailles, applications à fermer) : rien n'est supprimé sans validation.
        var preview = new PreviewWindow(selected) { Owner = Host };
        if (preview.ShowDialog() != true)
            return;

        _busy = true;
        CleanButton.IsEnabled = false;
        long totalFreed = 0;
        var errors = 0;

        foreach (var target in selected)
        {
            target.IsBusy = true;
            try
            {
                var (freed, skipped) = await Task.Run(() => CacheCleaner.Clean(target));
                totalFreed += freed;
                errors += skipped;
                target.Succeeded = skipped == 0;
                target.StatusText = skipped == 0
                    ? L.F("✓ {0} libérés", ValueParsers.FormatSize(freed))
                    : L.F(skipped > 1 ? "{0} libérés, {1} fichiers en cours d'utilisation ignorés" : "{0} libérés, {1} fichier en cours d'utilisation ignoré",
                        ValueParsers.FormatSize(freed), skipped);
                target.SizeBytes = await Task.Run(() => CacheCleaner.Measure(target));
            }
            catch (Exception ex)
            {
                target.Succeeded = false;
                target.StatusText = L.F("Erreur : {0}", ex.Message);
                errors++;
            }
            finally
            {
                target.IsBusy = false;
            }
        }

        CleanupHistory.Add("Caches", selected.Select(t => t.Name), totalFreed, errors);
        _busy = false;
        UpdateSelection();
        SelectionText.Text = L.F("Nettoyage terminé : {0} libérés.", ValueParsers.FormatSize(totalFreed));
        Anim.FadeSlideIn(SelectionText, 8);
        MemoryOptimizer.TrimSelfInBackground();
    }

    // ----- Mémoire vive -----

    private void UpdateRam()
    {
        var status = MemoryOptimizer.GetStatus();
        if (status.TotalBytes == 0)
            return;
        RamText.Text = L.F("{0} / {1}  ·  {2:0} %", ValueParsers.FormatSize((long)status.UsedBytes), ValueParsers.FormatSize((long)status.TotalBytes), status.UsedPercent);
        var parent = RamBar.Parent as FrameworkElement;
        if (parent is not null && parent.ActualWidth > 0)
            Anim.Width(RamBar, parent.ActualWidth * status.UsedPercent / 100.0);
    }

    private async void FreeRam_Click(object sender, RoutedEventArgs e)
    {
        RamButton.IsEnabled = false;
        RamResultText.Text = L.T("Optimisation en cours…");
        var freed = await Task.Run(MemoryOptimizer.Optimize);
        UpdateRam();
        RamResultText.Text = freed > 0
            ? L.F("✓ {0} de mémoire libérés. Effet temporaire : Windows la réutilisera selon ses besoins.", ValueParsers.FormatSize(freed))
            : L.T("La mémoire était déjà bien gérée : rien de plus à libérer pour le moment.");
        Anim.FadeSlideIn(RamResultText, 8);
        RamButton.IsEnabled = true;
    }

    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Task.Run(MemoryOptimizer.FlushDns);
        DnsResultText.Text = ok ? L.T("✓ Cache DNS vidé.") : L.T("Impossible de vider le cache DNS.");
        Anim.FadeSlideIn(DnsResultText, 8);
    }
}
