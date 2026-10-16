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

    public DiskView()
    {
        InitializeComponent();
        DrivesList.ItemsSource = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DriveChoice(d.RootDirectory.FullName,
                $"{d.Name.TrimEnd('\\')}  {ValueParsers.FormatSize(d.AvailableFreeSpace)} libres",
                $"{(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disque local" : d.VolumeLabel)} : " +
                $"{ValueParsers.FormatSize(d.TotalSize - d.AvailableFreeSpace)} utilisés sur {ValueParsers.FormatSize(d.TotalSize)}"))
            .ToList();

        _progressTimer.Tick += (_, _) =>
        {
            if (_scanner is null)
                return;
            StatusText.Text = $"Analyse… {_scanner.FilesScanned:N0} fichiers  ·  {ValueParsers.FormatSize(_scanner.BytesScanned)}  ·  {_scanner.CurrentFolder}";
        };
    }

    private async void Drive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DriveChoice drive })
            await ScanAsync(drive.Root);
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choisissez le dossier à analyser" };
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
        RowsList.ItemsSource = null;
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
            if (_scanner != scanner)
                return;
            var seconds = (DateTime.Now - started).TotalSeconds;
            Show(result);
            StatusText.Text += $"  ·  analysé en {seconds:0} s";
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   ex is AggregateException agg && agg.Flatten().InnerExceptions.All(i => i is OperationCanceledException))
        {
            if (_scanner == scanner)
            {
                StatusText.Text = "Analyse arrêtée.";
                HintText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Journal.Write("Analyse de l'espace disque impossible : " + ex);
            if (_scanner == scanner)
                StatusText.Text = "Analyse impossible : " + ex.Message;
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
        StatusText.Text = $"{ValueParsers.FormatSize(folder.SizeBytes)}  ·  {folder.FileCount:N0} fichier(s)";
        RowsList.ItemsSource = DiskScanner.RowsOf(folder);
        UpButton.IsEnabled = _trail.Count > 0;
        OpenCurrentButton.IsEnabled = true;
        HintText.Visibility = Visibility.Collapsed;
        Anim.FadeSlideIn(RowsList, 10, 280);
    }

    private void ShowFreeSpace(string root)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            Anim.CountBytes(FreeText, drive.AvailableFreeSpace);
            FreeLabel.Text = $"libres sur {drive.Name.TrimEnd('\\')}";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            FreeText.Text = "—";
        }
    }

    private void Rows_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowsList.SelectedItem is DiskNode { CanOpen: true } node && _current is not null)
        {
            _trail.Push(_current);
            Show(node);
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_trail.Count > 0)
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
            StatusText.Text = "Impossible d'ouvrir l'emplacement : " + ex.Message;
        }
    }
}
