using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>
/// Étape 1 : lance les désinstalleurs l'un après l'autre et cherche les restes de chacun.
/// Étape 2 : présente les restes ; rien n'est supprimé avant que l'utilisateur ait coché et confirmé.
/// En mode « désinstallation forcée », le désinstalleur officiel n'est pas lancé : on cherche directement
/// le dossier, les fichiers et les clés du logiciel (y compris son entrée « Uninstall »).
/// Le même mode est proposé automatiquement quand le désinstalleur officiel est absent ou en échec.
/// </summary>
public partial class UninstallWindow : Window
{
    private readonly ObservableCollection<QueueItem> _queue;
    private readonly ObservableCollection<LeftoverItem> _leftovers = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly SafetyGuard _guard = KnownLocations.CreateGuard();
    private readonly bool _forcedMode;
    /// <summary>Logiciels traités par désinstallation forcée (demandée ou proposée après un échec).</summary>
    private readonly List<InstalledProgram> _forcedPrograms = new();
    private bool _stopRequested;
    private bool _busy;

    /// <param name="forced">Vrai pour une désinstallation forcée : le désinstalleur officiel n'est pas lancé.
    /// L'appelant a déjà expliqué l'opération et obtenu la confirmation de l'utilisateur.</param>
    public UninstallWindow(IReadOnlyList<InstalledProgram> programs, bool forced = false)
    {
        InitializeComponent();
        _forcedMode = forced;
        _queue = new ObservableCollection<QueueItem>(programs.Select(p => new QueueItem(p)));
        QueueList.ItemsSource = _queue;

        if (forced)
        {
            Title = "Uninstallix · " + L.T("Désinstallation forcée");
            HeaderText.Text = L.T("Désinstallation forcée");
            SubHeaderText.Text = L.T("Le désinstalleur officiel n'est pas lancé : Uninstallix cherche lui-même les fichiers, dossiers et clés de registre du logiciel.");
        }

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
                HeaderText.Text = L.T("Préparation");
                FooterText.Text = L.T("Création d'un point de restauration Windows…");
                var error = await Task.Run(() => RestorePoint.Create(L.T("Uninstallix - avant désinstallation")));
                Journal.Write(error is null ? "Point de restauration demandé." : "Point de restauration impossible : " + error);
            }

            for (var i = 0; i < _queue.Count; i++)
            {
                var item = _queue[i];
                if (_stopRequested)
                {
                    item.Status = QueueStatus.Skipped;
                    item.Detail = L.T("Arrêté à votre demande");
                    continue;
                }

                FooterText.Text = L.F("En cours : {0}", item.Program.DisplayName);

                if (_forcedMode)
                {
                    HeaderText.Text = _queue.Count > 1
                        ? L.F("Désinstallation forcée {0} sur {1}", i + 1, _queue.Count)
                        : L.T("Désinstallation forcée");
                    if (item.Program.ForcedUninstallRefusal is { } refusal)
                    {
                        Journal.Write($"Désinstallation forcée refusée pour « {item.Program.DisplayName} » : élément protégé.");
                        item.Status = QueueStatus.Failed;
                        item.Detail = L.F("Désinstallation forcée refusée : {0}.", refusal);
                        continue;
                    }
                    await ForceAsync(item);
                    removedCount++;
                    continue;
                }

                HeaderText.Text = L.F("Désinstallation {0} sur {1}", i + 1, _queue.Count);
                item.Status = QueueStatus.Running;
                item.Detail = L.T("Le désinstalleur officiel est ouvert : suivez ses instructions.");

                var result = await UninstallRunner.RunAsync(item.Program, _closing.Token);

                switch (result.Outcome)
                {
                    case UninstallOutcome.Removed:
                        removedCount++;
                        item.Status = QueueStatus.Scanning;
                        item.Detail = L.T("Recherche des fichiers et clés restants…");
                        var found = await ScanLeftoversAsync(item.Program);
                        foreach (var leftover in found)
                            _leftovers.Add(leftover);
                        item.Status = QueueStatus.Removed;
                        item.Detail = found.Count switch
                        {
                            0 => L.T("Aucun reste trouvé."),
                            1 => L.T("1 reste trouvé."),
                            _ => L.F("{0} restes trouvés.", found.Count),
                        };
                        break;

                    case UninstallOutcome.MissingUninstaller:
                    case UninstallOutcome.UninstallerError:
                    case UninstallOutcome.Failed:
                        if (!OfferForcedUninstall(item, result))
                        {
                            item.Status = QueueStatus.Failed;
                            break;
                        }
                        await ForceAsync(item);
                        removedCount++;
                        break;

                    case UninstallOutcome.StillInstalled:
                    case UninstallOutcome.RebootRequired:
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

        HeaderText.Text = L.T("Terminé");
        SubHeaderText.Text = removedCount == 0
            ? L.T("Aucun logiciel n'a été désinstallé.")
            : L.T("Aucun fichier ni aucune clé de registre restants n'ont été trouvés.");
        FooterText.Text = string.Empty;
        CloseButton.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Le désinstalleur officiel a disparu ou a échoué : on propose une désinstallation forcée
    /// (fichiers + entrée de registre), toujours avec validation avant suppression.
    /// Jamais pour un logiciel protégé ou un favori protégé. Renvoie vrai si l'utilisateur accepte.
    /// </summary>
    private bool OfferForcedUninstall(QueueItem item, UninstallResult result)
    {
        var program = item.Program;
        item.Detail = result.Message;
        if (program.ForcedUninstallRefusal is { } refusal)
        {
            Journal.Write($"Désinstallation forcée non proposée pour « {program.DisplayName} » : élément protégé.");
            item.Detail = result.Message + "  " + L.F("Désinstallation forcée refusée : {0}.", refusal);
            return false;
        }

        var missing = result.Outcome == UninstallOutcome.MissingUninstaller;
        var intro = missing
            ? L.F("Le désinstalleur de « {0} » est introuvable : l'installation est probablement abîmée.", program.DisplayName)
            : L.F("Le désinstalleur de « {0} » n'a pas fonctionné : {1}", program.DisplayName, result.Message);
        var message = intro + "\n\n" +
            L.T("Uninstallix peut faire une désinstallation forcée : il cherche les fichiers, dossiers et l'entrée de registre " +
                "du logiciel, puis vous les présente. Rien ne sera supprimé sans votre accord.");
        if (!missing)
            message += "\n\n" + L.T("Si vous avez vous-même annulé la désinstallation, répondez Non.");
        message += "\n\n" + L.T("Fermez d'abord le logiciel s'il est ouvert. Continuer ?");

        var answer = MessageBox.Show(this, message,
            missing ? L.T("Désinstalleur introuvable") : L.T("Échec du désinstalleur"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, missing ? MessageBoxResult.Yes : MessageBoxResult.No);
        var accepted = answer == MessageBoxResult.Yes;
        Journal.Write(accepted
            ? $"Désinstallation forcée acceptée pour « {program.DisplayName} »."
            : $"Désinstallation forcée refusée par l'utilisateur pour « {program.DisplayName} ».");
        return accepted;
    }

    /// <summary>Désinstallation forcée d'un logiciel : cherche tout ce qui lui appartient, sans rien supprimer.</summary>
    private async Task ForceAsync(QueueItem item)
    {
        var program = item.Program;
        Journal.Write($"Désinstallation forcée de « {program.DisplayName} » ({program.FullKeyPath}) : recherche des éléments.");
        item.Status = QueueStatus.Scanning;
        item.Detail = L.T("Recherche des fichiers et de l'entrée de registre du logiciel…");
        var found = await ScanLeftoversAsync(program, forced: true);
        foreach (var leftover in found)
            _leftovers.Add(leftover);
        _forcedPrograms.Add(program);
        Journal.Write($"Désinstallation forcée de « {program.DisplayName} » : {found.Count} élément(s) proposé(s).");
        item.Status = QueueStatus.ForcedCleanup;
        item.Detail = found.Count switch
        {
            0 => L.T("Aucun fichier ni aucune clé de registre trouvés pour ce logiciel."),
            1 => L.T("1 élément à supprimer vous sera proposé."),
            _ => L.F("{0} éléments à supprimer vous seront proposés.", found.Count),
        };
    }

    private static async Task<List<LeftoverItem>> ScanLeftoversAsync(InstalledProgram program, bool forced = false)
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
                return scanner.Scan(program, forced)
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
        HeaderText.Text = L.T("Restes détectés");
        SubHeaderText.Text = L.T("Choisissez ce que vous voulez supprimer.");
        SelectAllButton.Visibility = Visibility.Visible;
        SelectNoneButton.Visibility = Visibility.Visible;
        KeepButton.Visibility = Visibility.Visible;
        DeleteButton.Visibility = Visibility.Visible;

        ForcedBanner.Visibility = _forcedPrograms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LeftoverGroupsList.ItemsSource = _leftovers
            .GroupBy(i => i.ProgramName)
            .Select(g => new LeftoverGroup(g.Key, g.ToList(), IsForced(g.Key)))
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

    private bool IsForced(string programName) => _forcedPrograms.Any(p => p.DisplayName == programName);

    /// <summary>L'entrée « Uninstall » d'un logiciel forcé : sans elle, il reste dans la liste des programmes.</summary>
    private LeftoverItem? ProgramEntry(InstalledProgram program) =>
        _leftovers.FirstOrDefault(i => i.Kind == LeftoverKind.RegistryKey && i.Hive == program.Hive
            && string.Equals(i.RegistrySubKey, program.KeyPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Programmes ouverts dont l'exécutable est dans l'un des dossiers donnés (noms affichables).</summary>
    private static List<string> RunningProcessesIn(IReadOnlyList<string> folders)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (folders.Count == 0)
            return names.ToList();
        var roots = folders.Select(f => SafetyGuard.NormalizePath(f) + "\\").ToList();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var file = process.MainModule?.FileName;
                if (file is null)
                    continue;
                var normalized = SafetyGuard.NormalizePath(file);
                if (roots.Any(r => normalized.StartsWith(r, StringComparison.Ordinal)))
                    names.Add(Path.GetFileName(file));
            }
            catch
            {
                // Processus système ou déjà terminé : inaccessible, ignoré.
            }
            finally
            {
                process.Dispose();
            }
        }
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<LeftoverItem> PendingSelection() =>
        _leftovers.Where(i => i.IsSelected && i.Succeeded != true).ToList();

    private void UpdateLeftoverFooter()
    {
        var selected = PendingSelection();
        var size = selected.Sum(i => i.SizeBytes ?? 0);
        FooterText.Text = selected.Count == 0
            ? L.F(_leftovers.Count > 1 ? "{0} éléments trouvés, aucun coché" : "{0} élément trouvé, aucun coché", _leftovers.Count)
            : L.F(selected.Count > 1 ? "{0} éléments cochés sur {1}" : "{0} élément coché sur {1}", selected.Count, _leftovers.Count)
              + (size > 0 ? "  ·  " + ValueParsers.FormatSize(size) : string.Empty);
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

        var message = size > 0
            ? L.F(selected.Count > 1 ? "Supprimer {0} éléments ({1}) ?" : "Supprimer {0} élément ({1}) ?", selected.Count, ValueParsers.FormatSize(size))
            : L.F(selected.Count > 1 ? "Supprimer {0} éléments ?" : "Supprimer {0} élément ?", selected.Count);
        message += "\n\n";
        if (files > 0)
            message += "•  " + (AppSettings.Current.UseRecycleBin
                ? L.F(files > 1 ? "{0} dossiers ou fichiers iront dans la Corbeille." : "{0} dossier ou fichier ira dans la Corbeille.", files)
                : L.F(files > 1
                    ? "{0} dossiers ou fichiers seront supprimés DÉFINITIVEMENT (Corbeille désactivée dans les réglages)."
                    : "{0} dossier ou fichier sera supprimé DÉFINITIVEMENT (Corbeille désactivée dans les réglages).", files)) + "\n";
        if (keys > 0)
            message += "•  " + L.F(keys > 1
                ? "{0} clés de registre seront sauvegardées dans « {1} » puis supprimées."
                : "{0} clé de registre sera sauvegardée dans « {1} » puis supprimée.", keys, Journal.RegistryBackupFolder) + "\n";
        if (lowConfidence > 0)
            message += "\n" + L.F(lowConfidence > 1
                ? "Attention : {0} éléments cochés sont marqués « À vérifier »."
                : "Attention : {0} élément coché est marqué « À vérifier ».", lowConfidence) + "\n";

        // Désinstallation forcée : entrée de registre non cochée, programme encore ouvert.
        var forcedSelected = selected.Where(i => IsForced(i.ProgramName)).ToList();
        if (forcedSelected.Count > 0)
        {
            foreach (var program in _forcedPrograms)
            {
                var entry = ProgramEntry(program);
                if (entry is not null && !entry.IsSelected && entry.Succeeded != true && forcedSelected.Any(i => i.ProgramName == program.DisplayName))
                    message += "\n" + L.F("« {0} » restera dans la liste des programmes : son entrée de registre n'est pas cochée.", program.DisplayName) + "\n";
            }

            var folders = forcedSelected.Where(i => i.Kind == LeftoverKind.Folder).Select(i => i.Path).ToList();
            SetLeftoverButtonsEnabled(false);
            FooterText.Text = L.T("Recherche des programmes encore ouverts…");
            var running = await Task.Run(() => RunningProcessesIn(folders));
            SetLeftoverButtonsEnabled(true);
            UpdateLeftoverFooter();
            if (running.Count > 0)
                message += "\n" + L.F("Ces programmes sont encore ouverts : {0}. Fermez-les d'abord, sinon certains fichiers ne pourront pas être supprimés.",
                    string.Join(", ", running.Take(6))) + "\n";
        }

        message += "\n" + L.T("Seuls les éléments cochés sont concernés.");

        var answer = MessageBox.Show(this, message, L.T("Confirmer la suppression"),
            MessageBoxButton.YesNo, lowConfidence > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _busy = true;
        SetLeftoverButtonsEnabled(false);
        var remover = new LeftoverRemover(_guard);
        foreach (var item in selected)
        {
            FooterText.Text = L.F("Suppression : {0}", item.Path);
            await Task.Run(() => remover.Remove(item));
        }
        _busy = false;

        var ok = selected.Count(i => i.Succeeded == true);
        var failed = selected.Count - ok;
        // Historique : une entrée « Désinstallation forcée » et une entrée « Restes de logiciels », selon les cas.
        var normalSelected = selected.Except(forcedSelected).ToList();
        if (normalSelected.Count > 0)
            CleanupHistory.Add("Restes de logiciels",
                normalSelected.Select(i => i.ProgramName).Distinct(),
                normalSelected.Where(i => i.Succeeded == true).Sum(i => i.SizeBytes ?? 0),
                normalSelected.Count(i => i.Succeeded != true));
        if (forcedSelected.Count > 0)
        {
            CleanupHistory.Add("Désinstallation forcée",
                forcedSelected.Select(i => i.ProgramName).Distinct(),
                forcedSelected.Where(i => i.Succeeded == true).Sum(i => i.SizeBytes ?? 0),
                forcedSelected.Count(i => i.Succeeded != true));
            foreach (var program in _forcedPrograms.Where(p => forcedSelected.Any(i => i.ProgramName == p.DisplayName)))
            {
                var gone = !ProgramInventory.StillExists(program);
                Journal.Write(gone
                    ? $"Désinstallation forcée de « {program.DisplayName} » terminée : le logiciel n'est plus dans la liste."
                    : $"Désinstallation forcée de « {program.DisplayName} » terminée : son entrée de registre est conservée.");
            }
        }
        HeaderText.Text = failed == 0 ? L.T("Nettoyage terminé") : L.T("Nettoyage terminé avec des erreurs");
        FooterText.Text = failed == 0
            ? L.F(ok > 1 ? "{0} éléments supprimés." : "{0} élément supprimé.", ok)
            : L.F((ok > 1, failed > 1) switch
            {
                (true, true) => "{0} éléments supprimés, {1} non supprimés (voir la colonne Résultat).",
                (true, false) => "{0} éléments supprimés, {1} non supprimé (voir la colonne Résultat).",
                (false, true) => "{0} élément supprimé, {1} non supprimés (voir la colonne Résultat).",
                _ => "{0} élément supprimé, {1} non supprimé (voir la colonne Résultat).",
            }, ok, failed);

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
        StopButton.Content = L.T("Arrêt après le logiciel en cours…");
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
            L.T("Une opération est en cours. Attendez qu'elle se termine, ou cliquez sur « Arrêter après celui-ci » " +
                "pour ne pas lancer les logiciels suivants."),
            "Uninstallix", MessageBoxButton.OK, MessageBoxImage.Information);
        e.Cancel = true;
    }
}
