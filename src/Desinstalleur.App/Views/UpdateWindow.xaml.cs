using System.Diagnostics;
using System.Windows;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>Présente une nouvelle version et ses nouveautés ; l'utilisateur choisit de l'installer ou non.</summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private CancellationTokenSource? _download;

    public UpdateWindow(UpdateInfo update)
    {
        InitializeComponent();
        _update = update;
        TitleText.Text = L.F("Uninstallix {0} est disponible", update.Version.ToString(3));
        VersionText.Text = L.F("Vous avez la version {0}  ·  téléchargement de {1}", SelfInstaller.Version, ValueParsers.FormatSize(update.Size));
        if (PortableMode.IsActive)
            InstallButtonText.Text = L.T("Mettre à jour la version portable");
        NotesText.Text = string.IsNullOrWhiteSpace(update.Notes) ? L.T("Pas de description pour cette version.") : CleanMarkdown(update.Notes);
        Loaded += (_, _) =>
        {
            Anim.Pop(Badge);
            Anim.FadeSlideIn(NotesText, 10, 400, 120);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    /// <summary>Affichage simple du texte Markdown de GitHub (titres et puces).</summary>
    private static string CleanMarkdown(string markdown) =>
        string.Join("\n", markdown.Replace("\r", string.Empty).Split('\n').Select(line =>
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
                return trimmed.TrimStart('#').Trim().ToUpperInvariant();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                return "•  " + trimmed[2..];
            return line.Replace("**", string.Empty);
        }));

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = L.T("Téléchargement…");
        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadBar.Value = p;
                ProgressText.Text = L.F("Téléchargement… {0:0} %", p);
            });
            var path = await UpdateChecker.DownloadAsync(_update, progress, _download.Token);
            ProgressText.Text = PortableMode.IsActive ? L.T("Remplacement de la version portable…") : L.T("Lancement de l'installation…");
            UpdateChecker.RunInstaller(path);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            InstallButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Journal.Write("Mise à jour impossible : " + ex);
            ProgressText.Text = L.F("La mise à jour a échoué : {0}", ex.Message);
            InstallButton.IsEnabled = true;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        _download?.Cancel();
        Close();
    }

    private void OpenPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_update.PageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Journal.Write("Ouverture de la page GitHub impossible : " + ex.Message);
        }
    }
}
