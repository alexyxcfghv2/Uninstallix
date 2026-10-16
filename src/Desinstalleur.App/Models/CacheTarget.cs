using Desinstalleur.Core;

namespace Desinstalleur.App.Models;

/// <summary>Un cache que l'on peut vider (FiveM, Discord, navigateur, Corbeille…).</summary>
public sealed class CacheTarget : ObservableObject
{
    private bool _isSelected;
    private long? _sizeBytes;
    private string? _statusText;
    private bool? _succeeded;
    private bool _isBusy;

    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Glyphe Segoe Fluent Icons.</summary>
    public required string Glyph { get; init; }

    /// <summary>Dossiers dont on vide le contenu (les dossiers eux-mêmes sont conservés).</summary>
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();

    /// <summary>Vrai pour la Corbeille, vidée par l'API Windows.</summary>
    public bool IsRecycleBin { get; init; }

    /// <summary>Ignore les fichiers modifiés récemment (dossiers temporaires).</summary>
    public bool SkipRecentFiles { get; init; }

    /// <summary>Sous-dossiers à ne jamais vider (ex. fichiers du jeu dans le cache FiveM).</summary>
    public IReadOnlyList<string> KeepSubfolders { get; init; } = Array.Empty<string>();

    /// <summary>Processus à fermer avant le nettoyage (sans « .exe »).</summary>
    public IReadOnlyList<string> Processes { get; init; } = Array.Empty<string>();

    /// <summary>Nom affiché quand le logiciel doit être fermé.</summary>
    public string? AppName { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public long? SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (Set(ref _sizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsEmpty => SizeBytes is 0;

    public string SizeText => SizeBytes switch
    {
        null => "Analyse…",
        0 => "Vide",
        _ => ValueParsers.FormatSize(SizeBytes),
    };

    public string? StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public bool? Succeeded
    {
        get => _succeeded;
        set => Set(ref _succeeded, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }
}
