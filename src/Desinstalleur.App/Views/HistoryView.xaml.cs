using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Desinstalleur.App.Services;

namespace Desinstalleur.App.Views;

/// <summary>Page « Historique » : nettoyages passés et espace récupéré.</summary>
public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        // Rechargé à chaque ouverture de la page : un nettoyage a pu avoir lieu entre-temps.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Reload();
        };
    }

    private void Reload()
    {
        var entries = CleanupHistory.Load();
        EntriesList.ItemsSource = entries;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = entries.Count > 0;
        CountText.Text = entries.Count.ToString(L.Culture);
        Anim.CountBytes(TotalText, entries.Sum(e => e.FreedBytes), 900);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(Window.GetWindow(this),
            L.T("Effacer tout l'historique des nettoyages ?\n\nCela ne touche à aucun fichier : seule la liste est vidée."),
            L.T("Effacer l'historique"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;
        CleanupHistory.Clear();
        Reload();
    }
}
