using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Models;

/// <summary>Conséquence du nettoyage d'un cache, affichée sous forme de pastille.</summary>
public enum CacheImpact
{
    /// <summary>Rien à refaire ni à retélécharger.</summary>
    None,

    /// <summary>Un peu plus lent la première fois, le temps de recréer le cache.</summary>
    Low,

    /// <summary>Effet visible à connaître (téléchargements, saccades…).</summary>
    Notice,

    /// <summary>Suppression définitive, sans retour possible.</summary>
    Permanent,
}

/// <summary>Un cache que l'on peut vider (FiveM, Discord, navigateur, Corbeille…).</summary>
public sealed class CacheTarget : ObservableObject
{
    private bool _isSelected;
    private long? _sizeBytes;
    private string? _statusText;
    private bool? _succeeded;
    private bool _isBusy;
    private bool _isExpanded;

    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Nom affiché (Name reste l'identifiant enregistré dans les profils).</summary>
    public string DisplayName => L.T(Name);
    public string DisplayDescription => L.T(Description);

    /// <summary>À quoi sert ce cache (texte français, traduit à l'affichage).</summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Ce qui se passe après le nettoyage (texte français, traduit à l'affichage).</summary>
    public string Consequence { get; init; } = string.Empty;

    public CacheImpact Impact { get; init; } = CacheImpact.Low;

    public string DisplayPurpose => L.T(Purpose);
    public string DisplayConsequence => L.T(Consequence);

    public string ImpactText => Impact switch
    {
        CacheImpact.None => L.T("Sans impact"),
        CacheImpact.Notice => L.T("À savoir"),
        CacheImpact.Permanent => L.T("Irréversible"),
        _ => L.T("Impact faible"),
    };

    public string ImpactToolTip => Impact switch
    {
        CacheImpact.None => L.T("Rien à refaire ni à télécharger après le nettoyage."),
        CacheImpact.Notice => L.T("Le nettoyage a un effet visible : lisez « Après le nettoyage »."),
        CacheImpact.Permanent => L.T("Les fichiers supprimés ne pourront pas être récupérés."),
        _ => L.T("Le cache se recrée tout seul : seul le premier chargement est un peu plus lent."),
    };

    /// <summary>Explication dépliée sous la ligne dans la page Nettoyage.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

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
        null => L.T("Analyse…"),
        0 => L.T("Vide"),
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
