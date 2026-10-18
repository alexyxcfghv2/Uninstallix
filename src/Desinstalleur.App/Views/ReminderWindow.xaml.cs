using System.Windows;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>
/// Petite fenêtre en bas à droite (au-dessus de la barre des tâches) affichée par « --rappel » :
/// annonce la place qu'un nettoyage pourrait libérer. L'utilisateur choisit ; rien n'est nettoyé ici.
/// </summary>
public partial class ReminderWindow : Window
{
    /// <summary>Vrai quand l'utilisateur a choisi « Voir le nettoyage ».</summary>
    public bool OpenCleanup { get; private set; }

    public ReminderWindow(long reclaimableBytes)
    {
        InitializeComponent();
        MessageText.Text = L.F("Un nettoyage pourrait libérer environ {0}", ValueParsers.FormatSize(reclaimableBytes));
        // Position provisoire près du coin, ajustée une fois la hauteur connue.
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 4;
        Top = area.Bottom - 200;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Coin bas-droit de la zone de travail (la barre des tâches est exclue).
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 4;
        Top = area.Bottom - ActualHeight - 4;
        Anim.FadeSlideIn(Panel, 16, 380);
        Anim.Pop(Badge, 160);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenCleanup = true;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void Never_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.CleanupReminder = false;
        AppSettings.Current.Save();
        CleanupReminder.Unregister();
        Journal.Write("Rappel de nettoyage désactivé depuis le rappel.");
        Close();
    }
}
