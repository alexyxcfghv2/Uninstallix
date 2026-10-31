using System.Windows.Media;
using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Models;

/// <summary>Une application ouverte proposée à la fermeture par le mode jeu (tous ses processus regroupés).</summary>
public sealed class GameModeApp : ObservableObject
{
    private bool _close;
    private long _ramBytes;
    private ImageSource? _icon;

    /// <summary>Clé stable des choix mémorisés : nom du processus en minuscules (ex. « chrome »).</summary>
    public required string Key { get; init; }
    public required string ProcessName { get; init; }
    public required string DisplayName { get; init; }
    public string? ExePath { get; init; }

    /// <summary>Identifiants des processus de cette application au moment de la lecture.</summary>
    public List<int> ProcessIds { get; } = new();

    /// <summary>Vrai si l'application a une fenêtre visible (sinon elle tourne en arrière-plan).</summary>
    public bool HasWindow { get; set; }

    /// <summary>Application vocale ou de diffusion : conseillé de la garder pendant le jeu.</summary>
    public bool IsVoice { get; init; }

    public bool IsBackground => !HasWindow;

    public bool Close
    {
        get => _close;
        set => Set(ref _close, value);
    }

    public long RamBytes
    {
        get => _ramBytes;
        set
        {
            if (Set(ref _ramBytes, value))
                OnPropertyChanged(nameof(RamText));
        }
    }

    public string RamText => ValueParsers.FormatSize(_ramBytes);

    public string DetailText => HasWindow
        ? ProcessName + ".exe"
        : L.F("{0}.exe · en arrière-plan", ProcessName);

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (Set(ref _icon, value))
                OnPropertyChanged(nameof(HasIcon));
        }
    }

    public bool HasIcon => _icon is not null;
}

/// <summary>Application fermée par le mode jeu, à rouvrir à la désactivation.</summary>
public sealed class GameModeClosedApp : ObservableObject
{
    private ImageSource? _icon;

    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    /// <summary>Arguments de la ligne de commande d'origine (vide si Windows ne les a pas donnés).</summary>
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (Set(ref _icon, value))
                OnPropertyChanged(nameof(HasIcon));
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasIcon => _icon is not null;
}

/// <summary>Contenu du fichier « mode-jeu.json » : choix mémorisés et état du mode jeu en cours.</summary>
public sealed class GameModeState
{
    /// <summary>Clé de l'application → vrai pour « Fermer », faux pour « Garder ».</summary>
    public Dictionary<string, bool> Choices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Option « Performances élevées » (activée par défaut).</summary>
    public bool UseHighPerformance { get; set; } = true;

    public bool Active { get; set; }
    public DateTime? ActivatedAt { get; set; }

    /// <summary>GUID du mode d'alimentation actif avant le mode jeu (null : rien à rétablir).</summary>
    public string? PreviousPowerScheme { get; set; }

    public List<GameModeClosedApp> ClosedApps { get; set; } = new();
}
