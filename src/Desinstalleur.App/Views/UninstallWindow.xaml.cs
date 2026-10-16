using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>
/// Étape 1 : lance les désinstalleurs l'un après l'autre et cherche les restes de chacun.
/// Étape 2 : présente les restes ; rien n'est supprimé avant que l'utilisateur ait coché et confirmé.
/// </summary>
public partial class UninstallWindow : Window
{
    private readonly ObservableCollection<QueueItem> _queue;
    private readonly ObservableCollection<LeftoverItem> _leftovers = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly SafetyGuard _guard = KnownLocations.CreateGuard();
    private bool _stopRequested;
    private bool _busy;

    public UninstallWindow(IReadOnlyList<InstalledProgram> programs)
    {
        InitializeComponent();
        _queue = new ObservableCollection<QueueItem>(programs.Select(p => new QueueItem(p)));
        QueueList.ItemsSource = _queue;

        Loaded += async (_, _) => await RunQueueAsync();
        Closing += OnClosing;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(this);
    }

    private async Task RunQueueAsync()
    {
        _busy = true;
        var removedCount = 0;

        try
        {
            if (AppSettings.Current.CreateRestorePoint)
            {
                HeaderText.Text = "Préparation";
                FooterText.Text = "Création d'un point de restauration Windows…";
                var error = await Task.Run(() => RestorePoint.Create("Uninstallix - avant désinstallation"));
                Journal.Write(error is null ? "Point de restauration demandé." : "Point de restauration impossible : " + error);
            }

            for (var i = 0; i < _queue.Count; i++)
            {
                var item = _queue[i];
                if (_stopRequested)
                {
                    item.Status = QueueStatus.Skipped;
                    item.Detail = "Arrêté à votre demande";
                    continue;
                }

                HeaderText.Text = $"Désinstallation {i + 1} sur {_queue.Count}";
                FooterText.Text = $"En cours : {item.Program.DisplayName}";
                item.Status = QueueStatus.Running;
                item.Detail = "Le désinstalleur officiel est ouvert : suivez ses instructions.";

                var result = await UninstallRunner.RunAsync(item.Program, _closing.Token);

                switch (result.Outcome)
                {
                    case UninstallOutcome.Removed:
                        removedCount++;
                        item.Status = QueueStatus.Scanning;
                        item.Detail = "Recherche des fichiers et clés restants…";
                        var found = await ScanLeftoversAsync(item.Program);
                        foreach (var leftover in found)
                            _leftovers.Add(leftover);
                        item.Status = QueueStatus.Removed;
                        item.Detail = found.Count switch
                        {
                            0 => "Aucun reste trouvé.",
                            1 => "1 reste trouvé.",
                            _ => $"{found.Count} restes trouvés.",
                        };
                        break;

                    case UninstallOutcome.MissingUninstaller:
                        if (!AskForcedCleanup(item.Program))
                        {
                            item.Status = QueueStatus.Failed;
                            item.Detail = result.Message;
                            break;
                        }
                        removedCount++;
                        item.Status = QueueStatus.Scanning;
                        item.Detail = "Recherche des fichiers et de l'entrée de registre du logiciel…";
                        var forced = await ScanLeftoversAsync(item.Program);
                        foreach (var leftover in forced)
                            _leftovers.Add(leftover);
                        item.Status = QueueStatus.ForcedCleanup;
                        item.Detail = forced.Count == 0
                            ? "Désinstalleur introuvable et aucun fichier trouvé."
                            : $"Désinstalleur introuvable : {forced.Count} élément(s) à nettoyer vous seront proposés.";
                        break;

                    case UninstallOutcome.StillInstalled:
                        item.Status = QueueStatus.StillInstalled;
                        item.Detail = result.Message;
                        break;

                    default:
                        item.Status = QueueStatus.Failed;
                        item.Detail = result.Message;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _busy = false;
        }

        StopButton.Visibility = Visibility.Collapsed;

        if (_leftovers.Count > 0)
        {
            ShowLeftovers();
            return;
        }

        HeaderText.Text = "Terminé";
        SubHeaderText.Text = removedCount == 0
            ? "Aucun logiciel n'a été désinstallé."
            : "Aucun fichier ni aucune clé de registre restants n'ont été trouvés.";
        FooterText.Text = string.Empty;
        CloseButton.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Le désinstalleur officiel a disparu : on propose de nettoyer à la main
    /// (fichiers + entrée de registre), toujours avec validation avant suppression.
    /// </summary>
    private bool AskForcedCleanup(InstalledProgram program)
    {
        var answer = MessageBox.Show(this,
            $"Le désinstalleur de « {program.DisplayName} » est introuvable : l'installation est probablement abîmée.\n\n" +
            "Uninstallix peut faire une désinstallation forcée : il cherche les fichiers, dossiers et l'entrée de registre " +
            "du logiciel, puis vous les présente. Rien ne sera supprimé sans votre accord.\n\n" +
            "Fermez d'abord le logiciel s'il est ouvert. Continuer ?",
            "Désinstalleur introuvable", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
        return answer == MessageBoxResult.Yes;
    }

    private static async Task<List<LeftoverItem>> ScanLeftoversAsync(InstalledProgram program)
    {
        try
        {
            return await Task.Run(() =>
            {
                // Liste fraîche : on protège tout ce qui est encore installé à cet instant,
                // sauf le logiciel lui-même (cas d'une désinstallation forcée).
                var stillInstalled = ProgramInventory.Load()
                    .Where(p => !(p.Hive == program.Hive && p.KeyPath.Equals(program.KeyPath, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                var scanner = new LeftoverScanner(KnownLocations.CreateGuard(), stillInstalled);
                // Favoris protégés : jamais proposés, ni eux ni un dossier qui les contient.
                return scanner.Scan(program)
                    .Where(i => i.Kind == LeftoverKind.RegistryKey ||
                                (!Favorites.IsProtectedPath(i.Path) && !Favorites.ContainsProtectedPath(i.Path)))
                    .ToList();
            });
        }
        catch (Exception ex)
        {
            Journal.Write($"Recherche des restes impossible pour « {program.DisplayName} » : {ex.Message}");
            return new List<LeftoverItem>();
        }
    }

    private void ShowLeftovers()
    {
        QueuePanel.Visibility = Visibility.Collapsed;
        LeftoversPanel.Visibility = Visibility.Visible;
        Anim.FadeSlideIn(LeftoversPanel, 16, 380);
        HeaderText.Text = "Restes détectés";
        SubHeaderText.Text = "Choisissez ce que vous voulez supprimer.";
        SelectAllButton.Visibility = Visibility.Visible;
        SelectNoneButton.Visibility = Visibility.Visible;
        KeepButton.Visibility = Visibility.Visible;
        DeleteButton.Visibility = Visibility.Visible;

        LeftoverGroupsList.ItemsSource = _leftovers
            .GroupBy(i => i.ProgramName)
            .Select(g => new LeftoverGroup(g.Key, g.ToList()))
            .ToList();

        foreach (var item in _leftovers)
        {
            if (!AppSettings.Current.AutoSelectSafeLeftovers)
                item.IsSelected = false;
            item.PropertyChanged += Leftover_PropertyChanged;
        }
        UpdateLeftoverFooter();
    }

    private void Leftover_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftoverItem.IsSelected))
            UpdateLeftoverFooter();
    }

    private List<LeftoverItem> PendingSelection() =>
        _leftovers.Where(i => i.IsSelected && i.Succeeded != true).ToList();

    private void UpdateLeftoverFooter()
    {
        var selected = PendingSelection();
        var size = selected.Sum(i => i.SizeBytes ?? 0);
        FooterText.Text = selected.Count == 0
            ? $"{_leftovers.Count} élément(s) trouvé(s), aucun coché"
            : $"{selected.Count} élément(s) coché(s) sur {_leftovers.Count}" + (size > 0 ? $"  ·  {ValueParsers.FormatSize(size)}" : string.Empty);
        DeleteButton.IsEnabled = selected.Count > 0;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _leftovers.Where(i => i.Succeeded != true))
            item.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _leftovers)
            item.IsSelected = false;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = PendingSelection();
        if (selected.Count == 0)
            return;

        var files = selected.Count(i => i.Kind != LeftoverKind.RegistryKey);
        var keys = selected.Count - files;
        var size = selected.Sum(i => i.SizeBytes ?? 0);
        var lowConfidence = selected.Count(i => i.Confidence == MatchConfidence.Low);

        var message = $"Supprimer {selected.Count} élément(s){(size > 0 ? $" ({ValueParsers.FormatSize(size)})" : string.Empty)} ?\n\n";
        if (files > 0)
            message += AppSettings.Current.UseRecycleBin
                ? $"•  {files} dossier(s) ou fichier(s) iront dans la Corbeille.\n"
                : $"•  {files} dossier(s) ou fichier(s) seront supprimés DÉFINITIVEMENT (Corbeille désactivée dans les réglages).\n";
        if (keys > 0)
            message += $"•  {keys} clé(s) de registre seront sauvegardées dans « {Journal.RegistryBackupFolder} » puis supprimées.\n";
        if (lowConfidence > 0)
            message += $"\nAttention : {lowConfidence} élément(s) coché(s) sont marqués « À vérifier ».\n";
        message += "\nSeuls les éléments cochés sont concernés.";

        var answer = MessageBox.Show(this, message, "Confirmer la suppression",
            MessageBoxButton.YesNo, lowConfidence > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _busy = true;
        SetLeftoverButtonsEnabled(false);
        var remover = new LeftoverRemover(_guard);
        foreach (var item in selected)
        {
            FooterText.Text = $"Suppression : {item.Path}";
            await Task.Run(() => remover.Remove(item));
        }
        _busy = false;

        var ok = selected.Count(i => i.Succeeded == true);
        var failed = selected.Count - ok;
        CleanupHistory.Add("Restes de logiciels",
            selected.Select(i => i.ProgramName).Distinct(),
            selected.Where(i => i.Succeeded == true).Sum(i => i.SizeBytes ?? 0),
            failed);
        HeaderText.Text = failed == 0 ? "Nettoyage terminé" : "Nettoyage terminé avec des erreurs";
        FooterText.Text = failed == 0
            ? $"{ok} élément(s) supprimé(s)."
            : $"{ok} élément(s) supprimé(s), {failed} non supprimé(s) (voir la colonne Résultat).";

        SetLeftoverButtonsEnabled(true);
        KeepButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        DeleteButton.IsEnabled = PendingSelection().Count > 0;
    }

    private void SetLeftoverButtonsEnabled(bool enabled)
    {
        SelectAllButton.IsEnabled = enabled;
        SelectNoneButton.IsEnabled = enabled;
        KeepButton.IsEnabled = enabled;
        DeleteButton.IsEnabled = enabled;
        CloseButton.IsEnabled = enabled;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _stopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "Arrêt après le logiciel en cours…";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_busy)
        {
            _closing.Cancel();
            return;
        }

        MessageBox.Show(this,
            "Une opération est en cours. Attendez qu'elle se termine, ou cliquez sur « Arrêter après celui-ci » " +
            "pour ne pas lancer les logiciels suivants.",
            "Uninstallix", MessageBoxButton.OK, MessageBoxImage.Information);
        e.Cancel = true;
    }
}
