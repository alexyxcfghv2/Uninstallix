using System.Windows;
using Desinstalleur.App.Services;

namespace Desinstalleur.App.Views;

/// <summary>Prépare un rapport de problème, avec aperçu modifiable avant de le copier ou l'enregistrer.</summary>
public partial class ReportWindow : Window
{
    public ReportWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Rebuild();
            DescriptionBox.Focus();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        // Appelé aussi pendant InitializeComponent.
        if (PreviewBox is not null && IsLoaded)
            Rebuild();
    }

    private void Rebuild() =>
        PreviewBox.Text = ProblemReport.Build(DescriptionBox.Text, JournalBox.IsChecked == true,
            SettingsBox.IsChecked == true, AnonymizeBox.IsChecked == true);

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PreviewBox.Text);
            ResultText.Text = L.T("✓ Rapport copié : collez-le (Ctrl+V) dans votre message à Claude.");
        }
        catch (Exception ex)
        {
            ResultText.Text = L.F("Copie impossible : {0}", ex.Message);
        }
        Anim.FadeSlideIn(ResultText, 6);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Enregistrer le rapport"),
            FileName = L.F("Rapport Uninstallix {0:yyyy-MM-dd HH-mm}.txt", DateTime.Now),
            Filter = L.T("Fichier texte (*.txt)|*.txt"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, PreviewBox.Text);
            ResultText.Text = L.T("✓ Rapport enregistré.");
        }
        catch (Exception ex)
        {
            ResultText.Text = L.F("Enregistrement impossible : {0}", ex.Message);
        }
        Anim.FadeSlideIn(ResultText, 6);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
