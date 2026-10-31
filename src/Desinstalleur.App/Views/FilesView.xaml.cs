using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Views;

/// <summary>
/// Page « Fichiers » : fichiers en double (une copie est toujours gardée par groupe) et gros fichiers
/// oubliés dans Téléchargements et sur le Bureau. Rien n'est coché d'office ; la suppression passe
/// par <see cref="UserFileRemover"/> (Corbeille par défaut) après confirmation.
/// </summary>
public partial class FilesView : UserControl
{
    // ----- Doublons -----
    private readonly List<string> _folders = DuplicateFinder.DefaultFolders();
    private List<DuplicateGroup> _groups = new();
    private CancellationTokenSource? _dupCancel;
    private bool _dupBusy;
    private bool _dupScanned;

    // ----- Gros fichiers oubliés -----
    private List<ForgottenFile> _old = new();
    private bool _oldBusy;
    private bool _oldScanned;

    public FilesView()
    {
        InitializeComponent();
        foreach (var radio in MinSizePanel.Children.OfType<RadioButton>())
        {
            if (long.TryParse(radio.Tag as string, out var bytes))
                radio.Content = ValueParsers.FormatSize(bytes);
        }
        RefreshFolders();
        UpdateHeader();
    }

    private bool DuplicatesMode => DuplicatesTab.IsChecked == true;

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (DuplicatesPanel is null || ForgottenPanel is null)
            return;
        DuplicatesPanel.Visibility = DuplicatesMode ? Visibility.Visible : Visibility.Collapsed;
        ForgottenPanel.Visibility = DuplicatesMode ? Visibility.Collapsed : Visibility.Visible;
        Anim.FadeSlideIn(DuplicatesMode ? DuplicatesPanel : ForgottenPanel, 8, 240);
        UpdateHeader();
    }

    /// <summary>Les deux pastilles de l'en-tête suivent l'outil affiché.</summary>
    private void UpdateHeader(bool animate = false)
    {
        int count;
        long total;
        bool scanned;
        if (DuplicatesMode)
        {
            var remaining = _groups.Where(g => g.Files.Count(f => f.Succeeded != true) > 1).ToList();
            count = remaining.Count;
            total = remaining.Sum(g => g.SizeBytes * (g.Files.Count(f => f.Succeeded != true) - 1));
            scanned = _dupScanned;
            CountLabel.Text = count > 1 ? L.T("groupes de doublons") : L.T("groupe de doublons");
            TotalLabel.Text = L.T("récupérables");
        }
        else
        {
            var remaining = _old.Where(f => f.Succeeded != true).ToList();
            count = remaining.Count;
            total = remaining.Sum(f => f.SizeBytes);
            scanned = _oldScanned;
            CountLabel.Text = count > 1 ? L.T("fichiers oubliés") : L.T("fichier oublié");
            TotalLabel.Text = L.T("au total");
        }

        if (!scanned)
        {
            CountText.Text = "—";
            TotalText.Text = "—";
            return;
        }
        CountText.Text = count.ToString(L.Culture);
        if (animate)
            Anim.CountBytes(TotalText, total);
        else
            TotalText.Text = ValueParsers.FormatSize(total);
    }

    private void OpenLocation(string path, TextBlock status)
    {
        try
        {
            var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            status.Text = L.F("Impossible d'ouvrir l'emplacement : {0}", ex.Message);
        }
    }

    private static string RecycleSentence(bool many) => AppSettings.Current.UseRecycleBin
        ? L.T(many ? "Ils iront dans la Corbeille : vous pourrez les restaurer." : "Il ira dans la Corbeille : vous pourrez le restaurer.")
        : L.T(many
            ? "Ils seront supprimés DÉFINITIVEMENT (Corbeille désactivée dans les réglages)."
            : "Il sera supprimé DÉFINITIVEMENT (Corbeille désactivée dans les réglages).");

    // =====================================================================
    // Doublons
    // =====================================================================

    private void RefreshFolders()
    {
        FoldersList.ItemsSource = null;
        FoldersList.ItemsSource = _folders.ToList();
        DupScanButton.IsEnabled = !_dupBusy && _folders.Count > 0;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Dossiers où chercher des doublons"), Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        foreach (var folder in dialog.FolderNames)
        {
            string full;
            try
            {
                full = Path.GetFullPath(folder).TrimEnd('\\');
                if (full.Length == 2 && full[1] == ':')
                    full += "\\";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (Favorites.IsProtectedPath(full))
            {
                DupStatusText.Text = L.F("{0} est un favori protégé : il n'est pas examiné.", full);
                continue;
            }
            if (!_folders.Contains(full, StringComparer.OrdinalIgnoreCase))
                _folders.Add(full);
        }
        RefreshFolders();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_dupBusy || sender is not FrameworkElement { DataContext: string folder })
            return;
        _folders.RemoveAll(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase));
        RefreshFolders();
    }

    private void ResetFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_dupBusy)
            return;
        _folders.Clear();
        _folders.AddRange(DuplicateFinder.DefaultFolders());
        RefreshFolders();
    }

    private long MinSize() =>
        MinSizePanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) is { Tag: string tag }
        && long.TryParse(tag, out var bytes) ? bytes : 1024 * 1024;

    private async void DupScan_Click(object sender, RoutedEventArgs e)
    {
        if (_dupBusy || _folders.Count == 0)
            return;

        _dupCancel = new CancellationTokenSource();
        var token = _dupCancel.Token;
        _dupBusy = true;
        SetDupScanning(true);
        DetachGroups();
        _groups = new List<DuplicateGroup>();
        GroupsList.ItemsSource = null;
        DupEmptyText.Visibility = Visibility.Collapsed;
        DupBanner.Visibility = Visibility.Collapsed;
        _dupScanned = false;
        UpdateHeader();

        var folders = _folders.ToList();
        var minSize = MinSize();
        var progress = new Progress<DuplicateProgress>(p =>
        {
            if (!_dupBusy)
                return;
            DupStatusText.Text = p.Text;
            DupProgress.IsIndeterminate = p.Fraction < 0;
            if (p.Fraction >= 0)
                DupProgress.Value = Math.Clamp(p.Fraction, 0, 1);
        });
        var started = DateTime.Now;
        try
        {
            var finder = new DuplicateFinder();
            _groups = await Task.Run(() => finder.Scan(folders, minSize, progress, token), token);
            foreach (var group in _groups)
                group.SelectionChanged += Group_SelectionChanged;
            _dupScanned = true;
            _dupBusy = false;
            DupStatusText.Text = L.F("Analyse terminée en {0:0} s.", (DateTime.Now - started).TotalSeconds);
            RefreshGroups(animate: true);
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   ex is AggregateException agg && agg.Flatten().InnerExceptions.All(i => i is OperationCanceledException))
        {
            _dupBusy = false;
            DupStatusText.Text = L.T("Analyse arrêtée.");
            DupEmptyText.Text = L.T("Choisissez les dossiers à comparer puis cliquez sur « Chercher les doublons ».");
            DupEmptyText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _dupBusy = false;
            Journal.Write("Doublons : analyse impossible : " + ex);
            DupStatusText.Text = L.F("Analyse impossible : {0}", ex.Message);
            DupEmptyText.Visibility = Visibility.Visible;
        }
        finally
        {
            _dupBusy = false;
            SetDupScanning(false);
            MemoryOptimizer.TrimSelfInBackground();
        }
    }

    private void DupCancel_Click(object sender, RoutedEventArgs e)
    {
        _dupCancel?.Cancel();
        DupCancelButton.IsEnabled = false;
    }

    private void SetDupScanning(bool scanning)
    {
        DupScanButton.IsEnabled = !scanning && _folders.Count > 0;
        DupCancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        DupCancelButton.IsEnabled = true;
        DupProgress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        DupProgress.IsIndeterminate = true;
        DupProgress.Value = 0;
        MinSizePanel.IsEnabled = !scanning;
        AddFolderButton.IsEnabled = !scanning;
        ResetFoldersButton.IsEnabled = !scanning;
        FoldersList.IsEnabled = !scanning;
        UpdateDupFooter();
    }

    private void RefreshGroups(bool animate)
    {
        GroupsList.ItemsSource = _groups;
        UpdateHeader(animate);
        if (_groups.Count == 0)
        {
            DupEmptyText.Text = L.T("Aucun doublon trouvé dans ces dossiers.");
            DupEmptyText.Visibility = Visibility.Visible;
            DupBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            DupEmptyText.Visibility = Visibility.Collapsed;
            DupBanner.Visibility = Visibility.Visible;
            if (animate)
                Anim.FadeSlideIn(GroupsList, 10, 280);
        }
        UpdateDupFooter();
    }

    private void Group_SelectionChanged(object? sender, EventArgs e) => UpdateDupFooter();

    private List<DuplicateFile> DupSelection() =>
        _groups.SelectMany(g => g.Files).Where(f => f.IsSelected && !f.IsKeep && f.Succeeded != true).ToList();

    private void UpdateDupFooter()
    {
        var selected = DupSelection();
        var size = selected.Sum(f => f.SizeBytes);
        DupFooterText.Text = selected.Count switch
        {
            0 => L.T("Aucune copie cochée."),
            1 => L.F("1 copie cochée  ·  {0}", ValueParsers.FormatSize(size)),
            _ => L.F("{0} copies cochées  ·  {1}", selected.Count, ValueParsers.FormatSize(size)),
        };
        var pending = _groups.Any(g => g.Files.Any(f => f.CanSelect && !f.IsSelected));
        SelectCopiesButton.IsEnabled = !_dupBusy && pending;
        DupSelectNoneButton.IsEnabled = !_dupBusy && selected.Count > 0;
        DupDeleteButton.IsEnabled = !_dupBusy && selected.Count > 0;
    }

    private void SelectCopies_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _groups)
            group.SelectCopies();
        UpdateDupFooter();
    }

    private void DupSelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _groups)
            group.ClearSelection();
        UpdateDupFooter();
    }

    private void KeepThis_Click(object sender, RoutedEventArgs e)
    {
        if (!_dupBusy && sender is FrameworkElement { DataContext: DuplicateFile file })
            file.Group?.SetKeep(file);
    }

    private void OpenDuplicateLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DuplicateFile file })
            OpenLocation(file.Path, DupStatusText);
    }

    private async void DupDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = DupSelection();
        if (selected.Count == 0 || _dupBusy)
            return;

        var size = selected.Sum(f => f.SizeBytes);
        var many = selected.Count > 1;
        var groupCount = selected.Select(f => f.Group).Distinct().Count();
        var message = L.F(many ? "Supprimer {0} copies ({1}) ?" : "Supprimer {0} copie ({1}) ?", selected.Count, ValueParsers.FormatSize(size)) + "\n\n";
        message += string.Join("\n", selected.Take(8).Select(f => "•  " + f.Path)) + "\n";
        if (selected.Count > 8)
            message += L.F(selected.Count - 8 > 1 ? "… et {0} autres" : "… et {0} autre", selected.Count - 8) + "\n";
        message += "\n" + RecycleSentence(many);
        message += "\n\n" + L.F(groupCount > 1
            ? "La copie marquée « À garder » de chacun des {0} groupes est conservée."
            : "La copie marquée « À garder » est conservée.", groupCount);

        var answer = MessageBox.Show(Window.GetWindow(this)!, message, L.T("Confirmer la suppression"), MessageBoxButton.YesNo,
            AppSettings.Current.UseRecycleBin ? MessageBoxImage.Question : MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _dupBusy = true;
        DupScanButton.IsEnabled = false;
        UpdateDupFooter();
        foreach (var group in selected.Select(f => f.Group!).Distinct())
        {
            // Dernier rempart : la copie gardée doit exister, être intacte et ne jamais être supprimée.
            var keep = group.Keep;
            var keepOk = keep is not null && !keep.IsSelected && KeepStillThere(keep);
            foreach (var file in group.Files.Where(f => f.IsSelected && !f.IsKeep && f.Succeeded != true).ToList())
            {
                if (!keepOk)
                {
                    file.Succeeded = false;
                    file.ResultText = L.T("La copie à garder a disparu ou a changé : rien n'est supprimé dans ce groupe.");
                    continue;
                }
                DupStatusText.Text = L.F("Suppression : {0}", file.Path);
                var (ok, text) = await Task.Run(() => UserFileRemover.Remove(file.Path, file.SizeBytes));
                file.ResultText = text;
                file.Succeeded = ok;
            }
        }

        var done = selected.Where(f => f.Succeeded == true).ToList();
        var failed = selected.Count - done.Count;
        var freed = done.Sum(f => f.SizeBytes);
        CleanupHistory.Add("Fichiers en double", selected.Select(f => f.Name), freed, failed);
        Journal.Write($"Doublons : {done.Count} copies supprimées, {failed} en échec, {freed} octets libérés.");

        _dupBusy = false;
        DupScanButton.IsEnabled = _folders.Count > 0;
        UpdateHeader();
        UpdateDupFooter();
        DupStatusText.Text = failed == 0
            ? L.F(done.Count > 1 ? "{0} copies supprimées  ·  {1} récupérés." : "{0} copie supprimée  ·  {1} récupérés.", done.Count, ValueParsers.FormatSize(freed))
            : L.F((done.Count > 1, failed > 1) switch
            {
                (true, true) => "{0} copies supprimées, {1} non supprimées (voir le détail dans la liste).",
                (true, false) => "{0} copies supprimées, {1} non supprimée (voir le détail dans la liste).",
                (false, true) => "{0} copie supprimée, {1} non supprimées (voir le détail dans la liste).",
                _ => "{0} copie supprimée, {1} non supprimée (voir le détail dans la liste).",
            }, done.Count, failed);
        Anim.FadeSlideIn(DupStatusText, 8);
    }

    private static bool KeepStillThere(DuplicateFile keep)
    {
        try
        {
            var info = new FileInfo(keep.Path);
            return info.Exists && info.Length == keep.SizeBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void DetachGroups()
    {
        foreach (var group in _groups)
            group.SelectionChanged -= Group_SelectionChanged;
    }

    // =====================================================================
    // Gros fichiers oubliés
    // =====================================================================

    private async void OldScan_Click(object sender, RoutedEventArgs e)
    {
        if (_oldBusy)
            return;

        var token = CancellationToken.None;
        _oldBusy = true;
        SetOldScanning(true);
        DetachOld();
        _old = new List<ForgottenFile>();
        OldList.ItemsSource = null;
        OldEmptyText.Visibility = Visibility.Collapsed;
        OldBanner.Visibility = Visibility.Collapsed;
        _oldScanned = false;
        UpdateHeader();

        var progress = new Progress<string>(text =>
        {
            if (_oldBusy)
                OldStatusText.Text = text;
        });
        try
        {
            _old = await Task.Run(() => ForgottenFiles.Scan(progress, token), token);
            foreach (var item in _old)
                item.PropertyChanged += Old_PropertyChanged;
            _oldScanned = true;
            _oldBusy = false;
            OldStatusText.Text = L.T("Analyse terminée.");
            RefreshOld(animate: true);
        }
        catch (OperationCanceledException)
        {
            _oldBusy = false;
            OldStatusText.Text = L.T("Analyse arrêtée.");
            OldEmptyText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _oldBusy = false;
            Journal.Write("Gros fichiers oubliés : analyse impossible : " + ex);
            OldStatusText.Text = L.F("Analyse impossible : {0}", ex.Message);
            OldEmptyText.Visibility = Visibility.Visible;
        }
        finally
        {
            _oldBusy = false;
            SetOldScanning(false);
        }
    }

    private void SetOldScanning(bool scanning)
    {
        OldScanButton.IsEnabled = !scanning;
        OldProgress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        UpdateOldFooter();
    }

    private void OldSort_Checked(object sender, RoutedEventArgs e)
    {
        if (_oldScanned && !_oldBusy)
            RefreshOld(animate: false);
    }

    private void RefreshOld(bool animate)
    {
        var sorted = SortByDate?.IsChecked == true
            ? _old.OrderBy(f => f.LastUsed).ThenByDescending(f => f.SizeBytes).ToList()
            : _old.OrderByDescending(f => f.SizeBytes).ThenBy(f => f.LastUsed).ToList();
        OldList.ItemsSource = sorted;
        UpdateHeader(animate);
        if (sorted.Count == 0)
        {
            OldEmptyText.Text = L.T("Aucun gros fichier oublié dans Téléchargements ni sur le Bureau.");
            OldEmptyText.Visibility = Visibility.Visible;
            OldBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            OldEmptyText.Visibility = Visibility.Collapsed;
            OldBanner.Visibility = Visibility.Visible;
            if (animate)
                Anim.FadeSlideIn(OldList, 10, 280);
        }
        UpdateOldFooter();
    }

    private void Old_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ForgottenFile.IsSelected))
            UpdateOldFooter();
    }

    private List<ForgottenFile> OldSelection() => _old.Where(f => f.IsSelected && f.Succeeded != true).ToList();

    private void UpdateOldFooter()
    {
        var selected = OldSelection();
        var size = selected.Sum(f => f.SizeBytes);
        OldFooterText.Text = selected.Count switch
        {
            0 => L.T("Aucun fichier coché."),
            1 => L.F("1 fichier coché  ·  {0}", ValueParsers.FormatSize(size)),
            _ => L.F("{0} fichiers cochés  ·  {1}", selected.Count, ValueParsers.FormatSize(size)),
        };
        OldSelectNoneButton.IsEnabled = !_oldBusy && selected.Count > 0;
        OldDeleteButton.IsEnabled = !_oldBusy && selected.Count > 0;
    }

    private void OldSelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _old)
            item.IsSelected = false;
    }

    private void OpenForgottenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ForgottenFile file })
            OpenLocation(file.Path, OldStatusText);
    }

    private async void OldDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = OldSelection();
        if (selected.Count == 0 || _oldBusy)
            return;

        var size = selected.Sum(f => f.SizeBytes);
        var many = selected.Count > 1;
        var message = L.F(many ? "Supprimer {0} fichiers ({1}) ?" : "Supprimer {0} fichier ({1}) ?", selected.Count, ValueParsers.FormatSize(size)) + "\n\n";
        message += string.Join("\n", selected.Take(8).Select(f => "•  " + f.Path)) + "\n";
        if (selected.Count > 8)
            message += L.F(selected.Count - 8 > 1 ? "… et {0} autres" : "… et {0} autre", selected.Count - 8) + "\n";
        message += "\n" + RecycleSentence(many);
        message += "\n\n" + L.T("Seuls les éléments cochés sont concernés.");

        var answer = MessageBox.Show(Window.GetWindow(this)!, message, L.T("Confirmer la suppression"), MessageBoxButton.YesNo,
            AppSettings.Current.UseRecycleBin ? MessageBoxImage.Question : MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _oldBusy = true;
        OldScanButton.IsEnabled = false;
        UpdateOldFooter();
        foreach (var file in selected)
        {
            OldStatusText.Text = L.F("Suppression : {0}", file.Path);
            var (ok, text) = await Task.Run(() => UserFileRemover.Remove(file.Path, file.SizeBytes));
            file.ResultText = text;
            file.Succeeded = ok;
        }

        var done = selected.Where(f => f.Succeeded == true).ToList();
        var failed = selected.Count - done.Count;
        var freed = done.Sum(f => f.SizeBytes);
        CleanupHistory.Add("Gros fichiers oubliés", selected.Select(f => f.Name), freed, failed);
        Journal.Write($"Gros fichiers oubliés : {done.Count} fichiers supprimés, {failed} en échec, {freed} octets libérés.");

        _oldBusy = false;
        OldScanButton.IsEnabled = true;
        UpdateHeader();
        UpdateOldFooter();
        OldStatusText.Text = failed == 0
            ? L.F(done.Count > 1 ? "{0} fichiers supprimés  ·  {1} récupérés." : "{0} fichier supprimé  ·  {1} récupérés.", done.Count, ValueParsers.FormatSize(freed))
            : L.F((done.Count > 1, failed > 1) switch
            {
                (true, true) => "{0} fichiers supprimés, {1} non supprimés (voir le détail dans la liste).",
                (true, false) => "{0} fichiers supprimés, {1} non supprimé (voir le détail dans la liste).",
                (false, true) => "{0} fichier supprimé, {1} non supprimés (voir le détail dans la liste).",
                _ => "{0} fichier supprimé, {1} non supprimé (voir le détail dans la liste).",
            }, done.Count, failed);
        Anim.FadeSlideIn(OldStatusText, 8);
    }

    private void DetachOld()
    {
        foreach (var item in _old)
            item.PropertyChanged -= Old_PropertyChanged;
    }
}
