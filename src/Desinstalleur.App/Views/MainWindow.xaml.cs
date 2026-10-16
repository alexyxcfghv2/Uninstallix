using System.Reflection;
using System.Windows;
using Desinstalleur.App.Services;

namespace Desinstalleur.App.Views;

/// <summary>Fenêtre principale : menu latéral et pages (Logiciels, Nettoyage, Réglages).</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "Uninstallix" : L.F("Version {0}.{1}.{2}", version.Major, version.Minor, version.Build);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Anim.SlideInFromLeft(Sidebar);
        Anim.FadeSlideIn(ProgramsPage, 18, 450, 120);
        if (AppSettings.Current.CheckUpdatesOnStartup)
            _ = CheckForUpdateAsync();
    }

    private UpdateInfo? _update;

    /// <summary>Vérification discrète au démarrage : un bandeau apparaît si une version plus récente existe.</summary>
    private async Task CheckForUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        try
        {
            _update = await UpdateChecker.CheckAsync();
        }
        catch (Exception ex)
        {
            Journal.Write("Vérification des mises à jour impossible : " + ex.Message);
            return;
        }
        if (_update is null)
            return;
        UpdateBannerText.Text = L.F("Version {0} · cliquez pour voir", _update.Version.ToString(3));
        UpdateBanner.Visibility = Visibility.Visible;
        Anim.FadeSlideIn(UpdateBanner, 12, 450);
    }

    private void UpdateBanner_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not null)
            new UpdateWindow(_update) { Owner = this }.ShowDialog();
    }

    private CleanupView? _cleanupPage;
    private DiskView? _diskPage;
    private HistoryView? _historyPage;
    private SettingsView? _settingsPage;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Appelé aussi pendant InitializeComponent, avant que les pages existent.
        if (ProgramsPage is null || PagesHost is null)
            return;

        UIElement page;
        if (sender == NavCleanup)
            page = _cleanupPage ??= AddPage(new CleanupView());
        else if (sender == NavDisk)
            page = _diskPage ??= AddPage(new DiskView());
        else if (sender == NavHistory)
            page = _historyPage ??= AddPage(new HistoryView());
        else if (sender == NavSettings)
            page = _settingsPage ??= AddPage(new SettingsView());
        else
            page = ProgramsPage;

        foreach (UIElement child in PagesHost.Children)
            child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;

        Anim.FadeSlideIn(page);

        if (page is CleanupView cleanup)
            cleanup.AnalyzeIfNeeded();
    }

    private T AddPage<T>(T page) where T : UIElement
    {
        PagesHost.Children.Add(page);
        return page;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Fenêtre réduite : on rend la mémoire inutilisée à Windows.
        if (WindowState == WindowState.Minimized)
            MemoryOptimizer.TrimSelfInBackground();
    }
}
