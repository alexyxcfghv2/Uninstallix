using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>
/// Page « Restes oubliés » : dossiers laissés par des logiciels déjà désinstallés.
/// Rien n'est coché d'office ; la suppression passe par <see cref="LeftoverRemover"/> après confirmation.
/// </summary>
public partial class OrphansView : UserControl
{
    private List<OrphanFolder> _all = new();
    private CancellationTokenSource? _cancel;
    private bool _busy;
    private bool _scanned;

    public OrphansView()
    {
        InitializeComponent();
    }

    // ----- Analyse -----

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _busy = true;
        SetScanning(true);
        Detach(_all);
        _all = new List<OrphanFolder>();
        OrphansList.ItemsSource = null;
        EmptyText.Visibility = Visibility.Collapsed;
        InfoBanner.Visibility = Visibility.Collapsed;
        CountText.Text = "—";
        TotalText.Text = "—";

        var progress = new Progress<string>(text =>
        {
            if (_busy)
                StatusText.Text = text;
        });
        var started = DateTime.Now;
        try
        {
            var scanner = new OrphanScanner();
            var found = await Task.Run(() => scanner.Scan(progress, token), token);
            _all = found;
            foreach (var item in _all)
                item.PropertyChanged += Item_PropertyChanged;
            _scanned = true;
            _busy = false;
            StatusText.Text = L.F("Analyse terminée en {0:0} s.", (DateTime.Now - started).TotalSeconds);
            Refresh(animate: true);
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   ex is AggregateException agg && agg.Flatten().InnerExceptions.All(i => i is OperationCanceledException))
        {
            _busy = false;
            StatusText.Text = L.T("Analyse arrêtée.");
            EmptyText.Text = L.T("Cliquez sur « Analyser » pour chercher les dossiers laissés par des logiciels déjà désinstallés.");
            EmptyText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _busy = false;
            Journal.Write("Restes oubliés : analyse impossible : " + ex);
            StatusText.Text = L.F("Analyse impossible : {0}", ex.Message);
            EmptyText.Visibility = Visibility.Visible;
        }
        finally
        {
            _busy = false;
            SetScanning(false);
            MemoryOptimizer.TrimSelfInBackground();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        CancelButton.IsEnabled = false;
    }

    private void SetScanning(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        CancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = true;
        ScanProgress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ShowSmallBox.IsEnabled = !scanning;
        UpdateFooter();
    }

    private void ShowSmall_Changed(object sender, RoutedEventArgs e)
    {
        if (!_scanned || _busy)
            return;
        // Les dossiers masqués ne restent pas cochés.
        if (ShowSmallBox.IsChecked != true)
        {
            foreach (var item in _all.Where(IsSmall))
                item.IsSelected = false;
        }
        Refresh(animate: false);
    }

    // ----- Affichage -----

    private static bool IsSmall(OrphanFolder item) => item.SizeBytes < OrphanScanner.SmallFolderBytes;

    private List<OrphanFolder> Visible() =>
        ShowSmallBox.IsChecked == true ? _all : _all.Where(i => !IsSmall(i)).ToList();

    private void Refresh(bool animate)
    {
        var visible = Visible();
        OrphansList.ItemsSource = visible;

        var remaining = visible.Where(i => i.Succeeded != true).ToList();
        CountText.Text = remaining.Count.ToString(L.Culture);
        CountLabel.Text = remaining.Count > 1 ? L.T("dossiers trouvés") : L.T("dossier trouvé");
        var total = remaining.Sum(i => i.SizeBytes);
        if (animate)
            Anim.CountBytes(TotalText, total);
        else
            TotalText.Text = ValueParsers.FormatSize(total);

        var hidden = ShowSmallBox.IsChecked == true ? 0 : _all.Count(i => IsSmall(i) && i.Succeeded != true);
        if (visible.Count == 0)
        {
            EmptyText.Text = hidden switch
            {
                0 => L.T("Aucun reste oublié trouvé : chaque dossier examiné appartient à un logiciel installé ou a servi récemment."),
                1 => L.T("Aucun reste oublié de plus de 1 Mo. 1 petit dossier est masqué."),
                _ => L.F("Aucun reste oublié de plus de 1 Mo. {0} petits dossiers sont masqués.", hidden),
            };
            EmptyText.Visibility = Visibility.Visible;
            InfoBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyText.Visibility = Visibility.Collapsed;
            InfoBanner.Visibility = Visibility.Visible;
            if (animate)
                Anim.FadeSlideIn(OrphansList, 10, 280);
        }

        if (hidden > 0 && visible.Count > 0)
            StatusText.Text = L.F(hidden > 1
                ? "{0} petits dossiers (moins de 1 Mo) sont masqués."
                : "{0} petit dossier (moins de 1 Mo) est masqué.", hidden);
        UpdateFooter();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrphanFolder.IsSelected))
            UpdateFooter();
    }

    private List<OrphanFolder> Selection() => Visible().Where(i => i.IsSelected && i.Succeeded != true).ToList();

    private void UpdateFooter()
    {
        var selected = Selection();
        var size = selected.Sum(i => i.SizeBytes);
        FooterText.Text = selected.Count switch
        {
            0 => L.T("Aucun dossier coché."),
            1 => L.F("1 dossier coché  ·  {0}", ValueParsers.FormatSize(size)),
            _ => L.F("{0} dossiers cochés  ·  {1}", selected.Count, ValueParsers.FormatSize(size)),
        };
        var pending = Visible().Any(i => i.Succeeded != true);
        SelectSafeButton.IsEnabled = !_busy && pending && Visible().Any(i => i.Confidence == OrphanConfidence.Safe && i.Succeeded != true);
        SelectNoneButton.IsEnabled = !_busy && selected.Count > 0;
        CleanButton.IsEnabled = !_busy && selected.Count > 0;
    }

    private void SelectSafe_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Visible().Where(i => i.Confidence == OrphanConfidence.Safe && i.Succeeded != true))
            item.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _all)
            item.IsSelected = false;
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OrphanFolder item })
            return;
        try
        {
            var arguments = Directory.Exists(item.Path) ? $"\"{item.Path}\"" : $"\"{Path.GetDirectoryName(item.Path)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Impossible d'ouvrir l'emplacement : {0}", ex.Message);
        }
    }

    // ----- Nettoyage -----

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selection();
        if (selected.Count == 0 || _busy)
            return;

        var size = selected.Sum(i => i.SizeBytes);
        var toCheck = selected.Count(i => i.Confidence == OrphanConfidence.Check);
        var many = selected.Count > 1;

        var message = L.F(many ? "Nettoyer {0} dossiers ({1}) ?" : "Nettoyer {0} dossier ({1}) ?", selected.Count, ValueParsers.FormatSize(size)) + "\n\n";
        message += string.Join("\n", selected.Take(8).Select(i => "•  " + i.Path)) + "\n";
        if (selected.Count > 8)
            message += L.F(selected.Count - 8 > 1 ? "… et {0} autres" : "… et {0} autre", selected.Count - 8) + "\n";
        message += "\n" + (AppSettings.Current.UseRecycleBin
            ? L.T(many ? "Ils iront dans la Corbeille : vous pourrez les restaurer." : "Il ira dans la Corbeille : vous pourrez le restaurer.")
            : L.T(many
                ? "Ils seront supprimés DÉFINITIVEMENT (Corbeille désactivée dans les réglages)."
                : "Il sera supprimé DÉFINITIVEMENT (Corbeille désactivée dans les réglages)."));
        if (toCheck > 0)
            message += "\n\n" + L.F(toCheck > 1
                ? "Attention : {0} éléments cochés sont marqués « À vérifier »."
                : "Attention : {0} élément coché est marqué « À vérifier ».", toCheck);
        message += "\n\n" + L.T("Seuls les éléments cochés sont concernés.");

        var answer = MessageBox.Show(Window.GetWindow(this)!, message, L.T("Confirmer la suppression"), MessageBoxButton.YesNo,
            toCheck > 0 || !AppSettings.Current.UseRecycleBin ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _busy = true;
        ScanButton.IsEnabled = false;
        ShowSmallBox.IsEnabled = false;
        UpdateFooter();
        var remover = new LeftoverRemover(KnownLocations.CreateGuard());
        foreach (var item in selected)
        {
            StatusText.Text = L.F("Suppression : {0}", item.Path);
            if (Favorites.IsProtectedPath(item.Path) || Favorites.ContainsProtectedPath(item.Path))
            {
                item.Succeeded = false;
                item.ResultText = L.T("Protégé par vos favoris : conservé.");
                continue;
            }

            var leftover = new LeftoverItem
            {
                ProgramName = item.Name,
                Kind = LeftoverKind.Folder,
                Path = item.Path,
                SizeBytes = item.SizeBytes,
                Confidence = item.Confidence == OrphanConfidence.Check ? MatchConfidence.Low : MatchConfidence.High,
                Reason = item.Reason,
            };
            await Task.Run(() => remover.Remove(leftover));
            item.ResultText = leftover.ResultText;
            item.Succeeded = leftover.Succeeded == true;
        }

        var ok = selected.Where(i => i.Succeeded == true).ToList();
        var failed = selected.Count - ok.Count;
        var freed = ok.Sum(i => i.SizeBytes);
        CleanupHistory.Add("Restes oubliés", selected.Select(i => i.Name), freed, failed);
        Journal.Write($"Restes oubliés : {ok.Count} dossiers nettoyés, {failed} en échec, {freed} octets libérés.");

        _busy = false;
        ScanButton.IsEnabled = true;
        ShowSmallBox.IsEnabled = true;
        Refresh(animate: false);
        StatusText.Text = failed == 0
            ? L.F(ok.Count > 1 ? "{0} dossiers nettoyés  ·  {1} récupérés." : "{0} dossier nettoyé  ·  {1} récupérés.", ok.Count, ValueParsers.FormatSize(freed))
            : L.F((ok.Count > 1, failed > 1) switch
            {
                (true, true) => "{0} dossiers nettoyés, {1} non supprimés (voir le détail dans la liste).",
                (true, false) => "{0} dossiers nettoyés, {1} non supprimé (voir le détail dans la liste).",
                (false, true) => "{0} dossier nettoyé, {1} non supprimés (voir le détail dans la liste).",
                _ => "{0} dossier nettoyé, {1} non supprimé (voir le détail dans la liste).",
            }, ok.Count, failed);
        Anim.FadeSlideIn(StatusText, 8);
    }

    private void Detach(IEnumerable<OrphanFolder> items)
    {
        foreach (var item in items)
            item.PropertyChanged -= Item_PropertyChanged;
    }
}
