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
        TitleText.Text = $"Uninstallix {update.Version.ToString(3)} est disponible";
        VersionText.Text = $"Vous avez la version {SelfInstaller.Version}  ·  téléchargement de {ValueParsers.FormatSize(update.Size)}";
        NotesText.Text = string.IsNullOrWhiteSpace(update.Notes) ? "Pas de description pour cette version." : CleanMarkdown(update.Notes);
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
        ProgressText.Text = "Téléchargement…";
        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadBar.Value = p;
                ProgressText.Text = $"Téléchargement… {p:0} %";
            });
            var path = await UpdateChecker.DownloadAsync(_update, progress, _download.Token);
            ProgressText.Text = "Lancement de l'installation…";
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
            ProgressText.Text = "La mise à jour a échoué : " + ex.Message;
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
