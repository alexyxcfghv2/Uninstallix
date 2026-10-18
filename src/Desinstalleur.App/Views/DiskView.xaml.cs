using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Desinstalleur.App.Services;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Views;

/// <summary>Page « Espace disque » : analyse des gros dossiers, en lecture seule.</summary>
public partial class DiskView : UserControl
{
    private sealed record DriveChoice(string Root, string Label, string Tooltip);

    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stack<DiskNode> _trail = new();
    private CancellationTokenSource? _cancel;
    private DiskScanner? _scanner;
    private DiskNode? _current;
    private DiskComparison? _comparison;
    private bool _showGrowth;

    public DiskView()
    {
        InitializeComponent();
        DrivesList.ItemsSource = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DriveChoice(d.RootDirectory.FullName,
                L.F("{0}  {1} libres", d.Name.TrimEnd('\\'), ValueParsers.FormatSize(d.AvailableFreeSpace)),
                L.F("{0} : {1} utilisés sur {2}", string.IsNullOrWhiteSpace(d.VolumeLabel) ? L.T("Disque local") : d.VolumeLabel,
                    ValueParsers.FormatSize(d.TotalSize - d.AvailableFreeSpace), ValueParsers.FormatSize(d.TotalSize))))
            .ToList();

        _progressTimer.Tick += (_, _) =>
        {
            if (_scanner is null)
                return;
            StatusText.Text = L.F("Analyse… {0:N0} fichiers  ·  {1}  ·  {2}", _scanner.FilesScanned, ValueParsers.FormatSize(_scanner.BytesScanned), _scanner.CurrentFolder);
        };
    }

    private async void Drive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DriveChoice drive })
            await ScanAsync(drive.Root);
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Choisissez le dossier à analyser") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await ScanAsync(dialog.FolderName);
    }

    private async Task ScanAsync(string root)
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        var scanner = new DiskScanner();
        _scanner = scanner;

        _trail.Clear();
        _current = null;
        _comparison = null;
        RowsList.ItemsSource = null;
        GrowthList.ItemsSource = null;
        CompareBar.Visibility = Visibility.Collapsed;
        FirstScanHint.Visibility = Visibility.Collapsed;
        SetGrowthMode(false);
        HintText.Visibility = Visibility.Collapsed;
        UpButton.IsEnabled = false;
        OpenCurrentButton.IsEnabled = false;
        PathText.Text = root;
        ScanProgress.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        DrivesList.IsEnabled = false;
        ShowFreeSpace(root);
        _progressTimer.Start();

        var started = DateTime.Now;
        try
        {
            var result = await Task.Run(() => scanner.Scan(root, token), token);
            var seconds = (DateTime.Now - started).TotalSeconds;
            // Analyse complète : comparée à la précédente du même emplacement, puis gardée pour la suivante.
            var comparison = await Task.Run(() => DiskSnapshots.CompareAndSave(result));
            if (_scanner != scanner)
                return;
            Show(result);
            ShowComparison(comparison);
            StatusText.Text += L.F("  ·  analysé en {0:0} s", seconds);
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   ex is AggregateException agg && agg.Flatten().InnerExceptions.All(i => i is OperationCanceledException))
        {
            if (_scanner == scanner)
            {
                StatusText.Text = L.T("Analyse arrêtée.");
                HintText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Journal.Write("Analyse de l'espace disque impossible : " + ex);
            if (_scanner == scanner)
                StatusText.Text = L.F("Analyse impossible : {0}", ex.Message);
        }
        finally
        {
            if (_scanner == scanner)
            {
                _progressTimer.Stop();
                ScanProgress.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                DrivesList.IsEnabled = true;
                MemoryOptimizer.TrimSelfInBackground();
            }
        }
    }

    private void Show(DiskNode folder)
    {
        _current = folder;
        PathText.Text = folder.FullPath;
        StatusText.Text = L.F(folder.FileCount > 1 ? "{0}  ·  {1:N0} fichiers" : "{0}  ·  {1:N0} fichier", ValueParsers.FormatSize(folder.SizeBytes), folder.FileCount);
        RowsList.ItemsSource = DiskScanner.RowsOf(folder);
        UpButton.IsEnabled = _trail.Count > 0;
        OpenCurrentButton.IsEnabled = true;
        HintText.Visibility = Visibility.Collapsed;
        Anim.FadeSlideIn(RowsList, 10, 280);
    }

    private void ShowComparison(DiskComparison? comparison)
    {
        _comparison = comparison;
        FirstScanHint.Visibility = comparison is null ? Visibility.Visible : Visibility.Collapsed;
        CompareBar.Visibility = comparison is null ? Visibility.Collapsed : Visibility.Visible;
        if (comparison is null)
            return;

        CompareText.Text = comparison.SummaryText;
        var unchanged = Math.Abs(comparison.Delta) < DiskSnapshots.UnchangedBytes;
        var brush = unchanged ? "TextPrimary" : comparison.Delta > 0 ? "DangerBrush" : "SuccessBrush";
        CompareText.Foreground = (System.Windows.Media.Brush)FindResource(brush);
        CompareIcon.Text = unchanged ? "\uE81C" : comparison.Delta > 0 ? "\uE70E" : "\uE70D";
        CompareIcon.Foreground = unchanged ? (System.Windows.Media.Brush)FindResource("TextSecondary") : CompareText.Foreground;
        GrowthList.ItemsSource = comparison.Grown;
        GrowthEmptyText.Text = L.F("Aucun dossier n'a grossi de plus de {0} depuis l'analyse précédente.",
            ValueParsers.FormatSize(DiskSnapshots.MinGrowthBytes));
    }

    private void ViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (GrowthList is null)
            return;
        var growth = GrownButton.IsChecked == true;
        if (growth == _showGrowth)
            return;
        _showGrowth = growth;
        ApplyViewMode();
    }

    private void SetGrowthMode(bool growth)
    {
        _showGrowth = growth;
        if (growth)
            GrownButton.IsChecked = true;
        else
            AllFoldersButton.IsChecked = true;
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var growth = _showGrowth && _comparison is not null;
        RowsList.Visibility = growth ? Visibility.Collapsed : Visibility.Visible;
        GrowthList.Visibility = growth ? Visibility.Visible : Visibility.Collapsed;
        GrowthEmptyText.Visibility = growth && _comparison!.Grown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpButton.IsEnabled = !growth && _trail.Count > 0;
        if (growth)
        {
            var count = _comparison!.Grown.Count;
            StatusText.Text = count switch
            {
                0 => L.T("Aucun dossier n'a beaucoup grossi."),
                1 => L.T("1 dossier a grossi."),
                _ => L.F("{0:N0} dossiers ont grossi.", count),
            };
            Anim.FadeSlideIn(GrowthList, 10, 280);
        }
        else if (_current is not null)
        {
            Show(_current);
        }
    }

    private void OpenGrowthLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GrowthRow row })
            OpenInExplorer(Directory.Exists(row.FullPath) ? $"/select,\"{row.FullPath}\"" : $"\"{Path.GetDirectoryName(row.FullPath)}\"");
    }

    private void ShowFreeSpace(string root)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            Anim.CountBytes(FreeText, drive.AvailableFreeSpace);
            FreeLabel.Text = L.F("libres sur {0}", drive.Name.TrimEnd('\\'));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            FreeText.Text = "—";
        }
    }

    private void Rows_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_showGrowth && RowsList.SelectedItem is DiskNode { CanOpen: true } node && _current is not null)
        {
            _trail.Push(_current);
            Show(node);
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (!_showGrowth && _trail.Count > 0)
            Show(_trail.Pop());
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    private void OpenCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
            OpenInExplorer($"\"{_current.FullPath}\"");
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DiskNode { IsReal: true } node })
            OpenInExplorer($"/select,\"{node.FullPath}\"");
    }

    private void OpenInExplorer(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Impossible d'ouvrir l'emplacement : {0}", ex.Message);
        }
    }
}
