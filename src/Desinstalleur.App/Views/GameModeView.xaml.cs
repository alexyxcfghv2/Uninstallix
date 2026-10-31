using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Desinstalleur.App.Models;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Views;

/// <summary>
/// Page « Mode jeu » : ferme gentiment les applications cochées, libère la RAM et passe en « Performances élevées »,
/// puis rouvre tout et rétablit le mode d'alimentation à la désactivation.
/// </summary>
public partial class GameModeView : UserControl
{
    private readonly ObservableCollection<GameModeApp> _apps = new();
    private readonly ObservableCollection<GameModeClosedApp> _closed = new();
    private readonly GameModeState _state;
    private List<GameModeApp> _refused = new();
    private bool _busy;
    private int _loadGeneration;

    public GameModeView()
    {
        InitializeComponent();
        AppsList.ItemsSource = _apps;
        ClosedList.ItemsSource = _closed;

        _state = GameMode.LoadState();
        HighPerformanceCheck.IsChecked = _state.UseHighPerformance;

        if (_state.Active)
        {
            // Mode jeu laissé actif lors d'une précédente session d'Uninstallix : on propose de tout rétablir.
            RestoreBanner.Visibility = Visibility.Visible;
            RestoreBannerText.Text = L.F(
                "Activé {0}, puis Uninstallix a été fermé. Quand vous avez fini de jouer, rouvrez vos applications et rétablissez le mode d'alimentation.",
                FormatWhen(_state.ActivatedAt));
            Journal.Write("Mode jeu : resté actif depuis une session précédente, rétablissement proposé.");
        }

        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
                await ReloadAsync();
        };
    }

    private Window Host => Window.GetWindow(this) ?? Application.Current.MainWindow;

    private static string FormatWhen(DateTime? when)
    {
        if (when is not { } date)
            return L.T("plus tôt");
        if (date.Date == DateTime.Today)
            return L.F("aujourd'hui à {0}", date.ToString(L.IsEnglish ? "h:mm tt" : "HH:mm", L.Culture));
        return L.F("le {0}", date.ToString(L.IsEnglish ? "MMM d, h:mm tt" : "d MMM à HH:mm", L.Culture));
    }

    // ----- Affichage -----

    private async Task ReloadAsync()
    {
        UpdateRam();
        if (_busy)
            return;
        ApplyMode();
        if (_state.Active)
        {
            await LoadClosedIconsAsync();
            return;
        }

        var generation = ++_loadGeneration;
        SetBusy(true, L.T("Lecture des applications ouvertes…"));
        List<GameModeApp> apps;
        try
        {
            apps = await Task.Run(() => GameMode.ListApps(_state));
        }
        catch (Exception ex)
        {
            Journal.Write("Mode jeu : lecture des applications impossible : " + ex.Message);
            apps = new List<GameModeApp>();
        }
        finally
        {
            SetBusy(false);
        }
        if (generation != _loadGeneration || _state.Active)
            return;

        _apps.Clear();
        foreach (var app in apps)
            _apps.Add(app);
        UpdateCounts();

        foreach (var app in apps)
        {
            if (app.ExePath is null)
                continue;
            var path = app.ExePath;
            var icon = await Task.Run(() => IconLoader.Load(path));
            if (generation != _loadGeneration)
                return;
            app.Icon = icon;
        }
    }

    private async Task LoadClosedIconsAsync()
    {
        foreach (var app in _closed.ToList())
        {
            if (app.Icon is not null || string.IsNullOrEmpty(app.ExePath))
                continue;
            var path = app.ExePath;
            app.Icon = await Task.Run(() => IconLoader.Load(path));
        }
    }

    /// <summary>Montre la liste des applications ouvertes (mode désactivé) ou celle des applications fermées (mode actif).</summary>
    private void ApplyMode()
    {
        var active = _state.Active;
        ActivateButton.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        DeactivateButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        HighPerformanceCheck.IsEnabled = !active;
        RefreshButton.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        AppsScroll.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        ClosedScroll.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ListHeader.Text = active ? L.T("FERMÉES PAR LE MODE JEU") : L.T("APPLICATIONS OUVERTES");

        if (active)
        {
            StateTitle.Text = L.T("Mode jeu activé");
            StateDetail.Text = _state.ClosedApps.Count == 0
                ? L.F("Actif depuis {0}. Aucune application à rouvrir.", FormatWhen(_state.ActivatedAt))
                : L.F("Actif depuis {0}. Les applications ci-dessous seront rouvertes à la désactivation.", FormatWhen(_state.ActivatedAt));
            if (_closed.Count != _state.ClosedApps.Count)
            {
                _closed.Clear();
                foreach (var app in _state.ClosedApps)
                    _closed.Add(app);
            }
        }
        else
        {
            StateTitle.Text = L.T("Mode jeu désactivé");
            StateDetail.Text = L.T("Cochez « Fermer » pour les applications à fermer. Vos choix sont mémorisés.");
            RestoreBanner.Visibility = Visibility.Collapsed;
        }
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        var count = _state.Active ? _state.ClosedApps.Count : _apps.Count(a => a.Close);
        SelectedCountText.Text = count.ToString(L.Culture);

        var empty = _state.Active ? _closed.Count == 0 : _apps.Count == 0;
        EmptyText.Text = _state.Active
            ? L.T("Aucune application n'a été fermée.")
            : L.T("Aucune application à fermer. Seules les applications de votre session avec une fenêtre, ou connues pour tourner en arrière-plan, sont proposées. Les jeux, lanceurs, anti-triche et outils des pilotes ne sont jamais fermés.");
        EmptyText.Visibility = empty && !_busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRam()
    {
        var status = MemoryOptimizer.GetStatus();
        FreeRamText.Text = status.TotalBytes == 0 ? "—" : ValueParsers.FormatSize((long)status.AvailableBytes);
    }

    private void SetBusy(bool busy, string? text = null)
    {
        _busy = busy;
        BusyText.Text = text ?? string.Empty;
        BusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActivateButton.IsEnabled = !busy;
        DeactivateButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        AppsList.IsEnabled = !busy;
        UpdateCounts();
    }

    private void ShowResult(string title, string text, bool warning)
    {
        ResultTitle.Text = title;
        ResultText.Text = text;
        ResultIcon.Text = warning ? "" : "";
        ResultIcon.Foreground = (System.Windows.Media.Brush)FindResource(warning ? "WarningBrush" : "SuccessBrush");
        ResultCard.Visibility = Visibility.Visible;
        Anim.FadeSlideIn(ResultCard);
    }

    private void ShowRefused()
    {
        if (_refused.Count == 0)
        {
            RefusedPanel.Visibility = Visibility.Collapsed;
            return;
        }
        RefusedText.Text = string.Join(", ", _refused.Select(a => a.DisplayName));
        RefusedPanel.Visibility = Visibility.Visible;
    }

    // ----- Choix -----

    private void SaveChoices()
    {
        foreach (var app in _apps)
            _state.Choices[app.Key] = app.Close;
        GameMode.SaveState(_state);
    }

    private void CloseCheck_Click(object sender, RoutedEventArgs e)
    {
        SaveChoices();
        UpdateCounts();
    }

    private void HighPerformance_Click(object sender, RoutedEventArgs e)
    {
        _state.UseHighPerformance = HighPerformanceCheck.IsChecked == true;
        GameMode.SaveState(_state);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    // ----- Activation -----

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _state.Active)
            return;
        SaveChoices();
        var toClose = _apps.Where(a => a.Close).ToList();
        var usePower = HighPerformanceCheck.IsChecked == true;
        var before = MemoryOptimizer.GetStatus().AvailableBytes;
        ResultCard.Visibility = Visibility.Collapsed;
        Journal.Write($"Mode jeu : activation ({toClose.Count} application(s) à fermer).");

        GameMode.CloseResult result;
        try
        {
            SetBusy(true, L.T("Fermeture des applications…"));
            result = toClose.Count == 0
                ? new GameMode.CloseResult(new(), new())
                : await Task.Run(() => GameMode.CloseGently(toClose, TimeSpan.FromSeconds(10)));

            _state.Active = true;
            _state.ActivatedAt = DateTime.Now;
            _state.ClosedApps = result.Closed;
            _state.PreviousPowerScheme = null;
            GameMode.SaveState(_state);

            string powerText = string.Empty;
            var powerWarning = false;
            if (usePower)
            {
                SetBusy(true, L.T("Passage en « Performances élevées »…"));
                var previous = await Task.Run(GameMode.GetActivePowerScheme);
                if (previous == GameMode.HighPerformanceGuid)
                {
                    powerText = L.T("Le mode « Performances élevées » était déjà actif.");
                }
                else if (previous is not null && await Task.Run(() => GameMode.SetPowerScheme("SCHEME_MIN")))
                {
                    _state.PreviousPowerScheme = previous;
                    GameMode.SaveState(_state);
                    powerText = L.T("Mode « Performances élevées » activé.");
                }
                else
                {
                    powerWarning = true;
                    powerText = L.T("Le mode « Performances élevées » n'a pas pu être activé : il n'existe peut-être pas sur ce PC.");
                }
            }

            SetBusy(true, L.T("Libération de la RAM…"));
            await Task.Run(MemoryOptimizer.Optimize);
            var after = MemoryOptimizer.GetStatus().AvailableBytes;
            var freed = after > before ? (long)(after - before) : 0;

            _refused = result.Refused;
            var closedText = result.Closed.Count switch
            {
                0 => L.T("Aucune application fermée."),
                1 => L.T("1 application fermée."),
                _ => L.F("{0} applications fermées.", result.Closed.Count),
            };
            var ramText = L.F("{0} de RAM libérée.", ValueParsers.FormatSize(freed));
            var text = string.Join(" ", new[] { closedText, ramText, powerText }.Where(s => s.Length > 0));
            ShowResult(L.T("Mode jeu activé"), text, powerWarning || _refused.Count > 0);
            ShowRefused();
        }
        catch (Exception ex)
        {
            Journal.Write("Mode jeu : erreur à l'activation : " + ex.Message);
            ShowResult(L.T("Mode jeu activé"), L.F("Une erreur est survenue : {0}", ex.Message), true);
        }
        finally
        {
            SetBusy(false);
        }

        _closed.Clear();
        ApplyMode();
        UpdateRam();
        await LoadClosedIconsAsync();
    }

    private async void ForceClose_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _refused.Count == 0)
            return;
        var names = string.Join("\n", _refused.Select(a => "• " + a.DisplayName));
        var answer = MessageBox.Show(Host,
            L.F("Forcer la fermeture de ces applications ?\n\n{0}\n\nElles ne pourront pas enregistrer : un travail non enregistré serait perdu.", names),
            "Uninstallix", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        var targets = _refused.ToList();
        try
        {
            SetBusy(true, L.T("Fermeture des applications…"));
            var before = MemoryOptimizer.GetStatus().AvailableBytes;
            var closed = await Task.Run(() => GameMode.ForceClose(targets));
            if (_state.Active)
            {
                _state.ClosedApps.AddRange(closed);
                GameMode.SaveState(_state);
            }
            var closedKeys = closed.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _refused = targets.Where(a => !closedKeys.Contains(a.Key)).ToList();

            await Task.Run(MemoryOptimizer.Optimize);
            var after = MemoryOptimizer.GetStatus().AvailableBytes;
            var freed = after > before ? (long)(after - before) : 0;
            var closedText = closed.Count switch
            {
                0 => L.T("Aucune application n'a pu être fermée."),
                1 => L.T("1 application fermée de force."),
                _ => L.F("{0} applications fermées de force.", closed.Count),
            };
            ShowResult(L.T("Fermeture forcée"), closedText + " " + L.F("{0} de RAM libérée.", ValueParsers.FormatSize(freed)),
                _refused.Count > 0);
            ShowRefused();
        }
        catch (Exception ex)
        {
            Journal.Write("Mode jeu : erreur à la fermeture forcée : " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
        ApplyMode();
        UpdateRam();
        await LoadClosedIconsAsync();
    }

    // ----- Désactivation -----

    private async void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_state.Active)
            return;
        ResultCard.Visibility = Visibility.Collapsed;
        _refused.Clear();
        RefusedPanel.Visibility = Visibility.Collapsed;
        Journal.Write("Mode jeu : désactivation.");

        var failures = new List<string>();
        var reopened = 0;
        var powerWarning = false;
        var powerText = string.Empty;
        try
        {
            SetBusy(true, L.T("Réouverture des applications…"));
            foreach (var app in _state.ClosedApps.ToList())
            {
                // Sur le fil de l'interface : le lancement passe par le bureau de Windows (COM, appartement STA).
                var error = GameMode.Reopen(app);
                if (error is null)
                    reopened++;
                else
                    failures.Add(app.DisplayName);
                await Task.Delay(350);
            }

            if (_state.PreviousPowerScheme is { } previous)
            {
                SetBusy(true, L.T("Rétablissement du mode d'alimentation…"));
                if (await Task.Run(() => GameMode.SetPowerScheme(previous)))
                {
                    powerText = L.T("Mode d'alimentation d'origine rétabli.");
                }
                else
                {
                    powerWarning = true;
                    powerText = L.T("Le mode d'alimentation d'origine n'a pas pu être rétabli : choisissez-le dans les paramètres d'alimentation de Windows.");
                }
            }

            _state.Active = false;
            _state.ActivatedAt = null;
            _state.PreviousPowerScheme = null;
            _state.ClosedApps = new List<GameModeClosedApp>();
            GameMode.SaveState(_state);
        }
        catch (Exception ex)
        {
            Journal.Write("Mode jeu : erreur à la désactivation : " + ex.Message);
            failures.Add(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }

        var reopenedText = reopened switch
        {
            0 => L.T("Aucune application à rouvrir."),
            1 => L.T("1 application rouverte."),
            _ => L.F("{0} applications rouvertes.", reopened),
        };
        var parts = new List<string> { reopenedText };
        if (failures.Count > 0)
            parts.Add(L.F("Impossible de rouvrir : {0}.", string.Join(", ", failures)));
        if (powerText.Length > 0)
            parts.Add(powerText);
        ShowResult(L.T("Mode jeu désactivé"), string.Join(" ", parts), powerWarning || failures.Count > 0);

        _closed.Clear();
        ApplyMode();
        // Laisser aux applications le temps de démarrer avant de relire la liste.
        await Task.Delay(2500);
        await ReloadAsync();
    }
}
