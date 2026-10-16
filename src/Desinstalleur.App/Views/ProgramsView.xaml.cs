using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

public partial class ProgramsView : UserControl
{
    private readonly BulkObservableCollection<InstalledProgram> _programs = new();
    private readonly ICollectionView _view;
    private int _loadGeneration;
    private bool _suppressUnlockEvents;
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(140) };

    public ProgramsView()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_programs);
        _view.Filter = FilterProgram;
        ProgramsGrid.ItemsSource = _view;
        ApplySort(0);
        ShowSystemBox.IsChecked = AppSettings.Current.ShowSystemComponents;
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            RefreshView();
        };

        var firstLoad = true;
        Loaded += async (_, _) =>
        {
            if (!firstLoad)
                return;
            firstLoad = false;
            await ReloadAsync();
        };
    }

    /// <summary>Fenêtre qui contient la page (pour placer les boîtes de dialogue au bon endroit).</summary>
    private Window Host => Window.GetWindow(this) ?? Application.Current.MainWindow;

    // ----- Chargement -----

    public async Task ReloadAsync()
    {
        var generation = ++_loadGeneration;
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;

        List<InstalledProgram> list;
        try
        {
            list = await Task.Run(ProgramInventory.Load);
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            MessageBox.Show(Host, "Impossible de lire la liste des logiciels : " + ex.Message, "Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (generation != _loadGeneration)
            return;

        foreach (var p in _programs)
            p.PropertyChanged -= Program_PropertyChanged;

        // Un seul tri/filtrage pour toute la liste, au lieu d'un par logiciel ajouté.
        var unlocked = UnlockProtectedBox.IsChecked == true;
        foreach (var p in list)
        {
            p.CanSelect = !p.IsProtected || unlocked;
            p.PropertyChanged += Program_PropertyChanged;
        }
        _programs.ReplaceAll(list);

        LoadingPanel.Visibility = Visibility.Collapsed;
        RefreshView();
        UpdateSelectionSummary();
        Anim.FadeSlideIn(ProgramsGrid, 16, 380);

        await LoadIconsAndSizesAsync(list, generation);
    }

    /// <summary>Charge les icônes et mesure les dossiers en arrière-plan, sans bloquer l'interface.</summary>
    private async Task LoadIconsAndSizesAsync(List<InstalledProgram> list, int generation)
    {
        // Plusieurs logiciels traités en même temps ; les résultats remontent à l'écran par paquets.
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6) };
        await Task.Run(() =>
        {
            var icons = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource?>(StringComparer.OrdinalIgnoreCase);
            var pending = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            var flushing = 0;

            void Push(Action update)
            {
                pending.Enqueue(update);
                if (Interlocked.Exchange(ref flushing, 1) == 0)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        Interlocked.Exchange(ref flushing, 0);
                        while (pending.TryDequeue(out var action))
                            action();
                    });
                }
            }

            Parallel.ForEach(list, parallel, (program, state) =>
            {
                if (generation != _loadGeneration)
                {
                    state.Stop();
                    return;
                }
                if (string.IsNullOrWhiteSpace(program.DisplayIconPath))
                    return;
                // Même fichier d'icône pour plusieurs logiciels : on ne l'extrait qu'une fois.
                var icon = icons.GetOrAdd(program.DisplayIconPath, IconLoader.Load);
                if (icon is not null)
                    Push(() => program.Icon = icon);
            });

            Parallel.ForEach(list.Where(p => p.SizeBytes is null), parallel, (program, state) =>
            {
                if (generation != _loadGeneration)
                {
                    state.Stop();
                    return;
                }
                var size = ProgramInventory.MeasureFolder(program.InstallLocation);
                if (size is not null)
                {
                    Push(() =>
                    {
                        program.SizeIsEstimated = true;
                        program.SizeBytes = size;
                    });
                }
            });

            // Dernier paquet éventuel.
            Dispatcher.Invoke(() =>
            {
                while (pending.TryDequeue(out var action))
                    action();
            }, DispatcherPriority.Background);
        });

        if (generation == _loadGeneration)
        {
            UpdateSummary();
            // Le chargement a créé beaucoup d'objets temporaires : on rend la mémoire à Windows.
            MemoryOptimizer.TrimSelfInBackground();
        }
    }

    // ----- Recherche, tri, filtres -----

    private bool FilterProgram(object obj)
    {
        if (obj is not InstalledProgram p)
            return false;
        if (p.IsSystemComponent && ShowSystemBox.IsChecked != true)
            return false;

        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
            return true;

        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var haystack = $"{p.DisplayName} {p.Publisher}";
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .All(word => compare.IndexOf(haystack, word, options) >= 0);
    }

    private void RefreshView()
    {
        _view.Refresh();
        EmptyText.Visibility = _view.IsEmpty && LoadingPanel.Visibility != Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSummary();
    }

    // La recherche attend une courte pause dans la frappe avant de filtrer.
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void ShowSystemBox_Changed(object sender, RoutedEventArgs e) => RefreshView();

    private void Sort_Checked(object sender, RoutedEventArgs e)
    {
        // Appelé aussi pendant InitializeComponent, avant que la vue existe.
        if (_view is null || sender is not RadioButton { Tag: string tag })
            return;
        ApplySort(int.Parse(tag, CultureInfo.InvariantCulture));
    }

    private void ApplySort(int index)
    {
        using (_view.DeferRefresh())
        {
            _view.SortDescriptions.Clear();
            switch (index)
            {
                case 1:
                    _view.SortDescriptions.Add(new SortDescription(nameof(InstalledProgram.SortSize), ListSortDirection.Descending));
                    break;
                case 2:
                    _view.SortDescriptions.Add(new SortDescription(nameof(InstalledProgram.SortDate), ListSortDirection.Descending));
                    break;
                case 3:
                    _view.SortDescriptions.Add(new SortDescription(nameof(InstalledProgram.Publisher), ListSortDirection.Ascending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(InstalledProgram.DisplayName), ListSortDirection.Ascending));
                    break;
                default:
                    _view.SortDescriptions.Add(new SortDescription(nameof(InstalledProgram.DisplayName), ListSortDirection.Ascending));
                    break;
            }
        }

        foreach (var column in ProgramsGrid.Columns)
            column.SortDirection = null;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    // ----- Protection -----

    private void UnlockProtectedBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressUnlockEvents)
            return;

        var answer = MessageBox.Show(Host,
            "Les éléments protégés sont des composants dont Windows ou d'autres logiciels ont besoin " +
            "(pilotes, Visual C++, .NET, WebView2…). Les désinstaller peut empêcher des programmes, " +
            "voire votre matériel, de fonctionner.\n\nVoulez-vous vraiment autoriser leur sélection ?",
            "Déverrouiller les éléments protégés", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _suppressUnlockEvents = true;
            UnlockProtectedBox.IsChecked = false;
            _suppressUnlockEvents = false;
            return;
        }

        foreach (var p in _programs)
            p.CanSelect = true;
    }

    private void UnlockProtectedBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressUnlockEvents)
            return;
        foreach (var p in _programs)
            p.CanSelect = !p.IsProtected;
    }

    // ----- Sélection -----

    private void Program_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstalledProgram.IsSelected) or nameof(InstalledProgram.SizeBytes))
            UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var selected = _programs.Where(p => p.IsSelected).ToList();
        UninstallButton.IsEnabled = selected.Count > 0;

        if (selected.Count == 0)
        {
            SelectionText.Text = "Aucun logiciel sélectionné";
            return;
        }

        var size = selected.Sum(p => p.SizeBytes ?? 0);
        var label = selected.Count == 1 ? "1 logiciel sélectionné" : $"{selected.Count} logiciels sélectionnés";
        SelectionText.Text = size > 0 ? $"{label}  ·  {ValueParsers.FormatSize(size)}" : label;
    }

    private void UpdateSummary()
    {
        var visible = _view.Cast<InstalledProgram>().ToList();
        var total = visible.Sum(p => p.SizeBytes ?? 0);
        var protectedCount = visible.Count(p => p.IsProtected);
        CountText.Text = visible.Count.ToString(CultureInfo.CurrentCulture);
        Anim.CountBytes(TotalSizeText, total);
        ProtectedCountText.Text = protectedCount.ToString(CultureInfo.CurrentCulture);
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _programs)
            p.IsSelected = false;
    }

    private void ProgramsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is InstalledProgram p && p.CanSelect)
            p.IsSelected = !p.IsSelected;
    }

    // ----- Menu contextuel -----

    private async void UninstallOne_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is not InstalledProgram p)
            return;
        if (!p.CanSelect)
        {
            MessageBox.Show(Host, $"« {p.DisplayName} » est protégé : {p.ProtectionReason}.\n\n" +
                "Cochez « Déverrouiller les protégés » si vous voulez vraiment le désinstaller.",
                "Élément protégé", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await StartUninstallAsync(new List<InstalledProgram> { p });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is not InstalledProgram p)
            return;
        if (string.IsNullOrWhiteSpace(p.InstallLocation) || !Directory.Exists(p.InstallLocation))
        {
            MessageBox.Show(Host, "Ce logiciel n'indique pas de dossier d'installation.", "Uninstallix",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p.InstallLocation}\"") { UseShellExecute = true });
    }

    private void ProgramMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is InstalledProgram p)
            FavoriteMenuItem.Header = Favorites.IsProtectedProgram(p.DisplayName)
                ? "Retirer des favoris protégés"
                : "Ajouter aux favoris protégés";
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is not InstalledProgram p)
            return;
        if (Favorites.IsProtectedProgram(p.DisplayName))
            Favorites.RemoveProgram(p.DisplayName);
        else
            Favorites.AddProgram(p.DisplayName);
        await ReloadAsync();
    }

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramsGrid.SelectedItem is InstalledProgram p)
            Clipboard.SetText(p.DisplayName);
    }

    // ----- Désinstallation -----

    private async void UninstallSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _programs.Where(p => p.IsSelected).ToList();
        if (selected.Count > 0)
            await StartUninstallAsync(selected);
    }

    private async Task StartUninstallAsync(List<InstalledProgram> selected)
    {
        var names = string.Join("\n", selected.Take(12).Select(p => "•  " + p.DisplayName));
        if (selected.Count > 12)
            names += $"\n…et {selected.Count - 12} autre(s)";

        var confirm = MessageBox.Show(Host,
            $"Désinstaller {(selected.Count == 1 ? "ce logiciel" : $"ces {selected.Count} logiciels")} ?\n\n{names}\n\n" +
            "Le désinstalleur officiel de chaque logiciel va s'ouvrir, l'un après l'autre. " +
            "Ensuite, les fichiers restants vous seront présentés : rien ne sera supprimé sans votre accord.",
            "Confirmer la désinstallation", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
            return;

        var protectedOnes = selected.Where(p => p.IsProtected).ToList();
        if (protectedOnes.Count > 0)
        {
            var list = string.Join("\n", protectedOnes.Select(p => $"•  {p.DisplayName} ({p.ProtectionReason})"));
            var again = MessageBox.Show(Host,
                $"Attention : la sélection contient des composants essentiels.\n\n{list}\n\n" +
                "Les supprimer peut empêcher d'autres logiciels ou votre matériel de fonctionner. Continuer quand même ?",
                "Composants protégés", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (again != MessageBoxResult.Yes)
                return;
        }

        var window = new UninstallWindow(selected) { Owner = Host };
        window.ShowDialog();
        await ReloadAsync();
    }
}
