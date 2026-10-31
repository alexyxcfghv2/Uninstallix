using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Models;

/// <summary>
/// Un fichier d'un groupe de doublons. Le fichier « à garder » ne peut jamais être coché :
/// un groupe garde donc toujours au moins une copie.
/// </summary>
public sealed class DuplicateFile : ObservableObject
{
    private bool _isSelected;
    private bool _isKeep;
    private string? _resultText;
    private bool? _succeeded;

    public required string Path { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastModified { get; init; }

    /// <summary>Groupe auquel appartient le fichier (renseigné à la création du groupe).</summary>
    public DuplicateGroup? Group { get; internal set; }

    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? Path;

    public string ModifiedText => L.F("Modifié le {0}",
        LastModified.ToString(L.IsEnglish ? "MMM d, yyyy" : "d MMM yyyy", L.Culture));

    /// <summary>Copie conservée : jamais cochée, jamais supprimée.</summary>
    public bool IsKeep
    {
        get => _isKeep;
        internal set
        {
            if (Set(ref _isKeep, value))
            {
                if (value)
                    IsSelected = false;
                OnPropertyChanged(nameof(CanSelect));
            }
        }
    }

    /// <summary>Vrai si la case peut être cochée (ni copie gardée, ni déjà supprimée).</summary>
    public bool CanSelect => !_isKeep && _succeeded != true;

    /// <summary>Coché = cette copie sera supprimée.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var allowed = value && !_isKeep && _succeeded != true && (Group?.CanDelete(this) ?? false);
            if (Set(ref _isSelected, allowed))
                Group?.NotifySelection();
            else if (value && !allowed)
                OnPropertyChanged();  // remet la case à jour si le clic a été refusé
        }
    }

    public string? ResultText
    {
        get => _resultText;
        set => Set(ref _resultText, value);
    }

    public bool? Succeeded
    {
        get => _succeeded;
        set
        {
            if (Set(ref _succeeded, value) && value == true)
                IsSelected = false;
            OnPropertyChanged(nameof(CanSelect));
        }
    }
}

/// <summary>Plusieurs fichiers au contenu identique (même taille et même empreinte SHA-256).</summary>
public sealed class DuplicateGroup : ObservableObject
{
    public DuplicateGroup(long sizeBytes, string hash, IEnumerable<DuplicateFile> files)
    {
        SizeBytes = sizeBytes;
        Hash = hash;
        Files = files.ToList();
        foreach (var file in Files)
            file.Group = this;
        ChooseKeep();
    }

    public long SizeBytes { get; }
    public string Hash { get; }
    public IReadOnlyList<DuplicateFile> Files { get; }

    /// <summary>Levé quand une case du groupe change.</summary>
    public event EventHandler? SelectionChanged;

    public DuplicateFile? Keep => Files.FirstOrDefault(f => f.IsKeep);

    public string Title => Keep?.Name ?? Files[0].Name;

    /// <summary>Place récupérable si l'on ne garde qu'une copie.</summary>
    public long WastedBytes => SizeBytes * Math.Max(0, Files.Count - 1);

    public long SelectedBytes => SizeBytes * Files.Count(f => f.IsSelected);

    public string CountText => L.F(Files.Count > 1 ? "{0} copies × {1}" : "{0} copie × {1}", Files.Count, ValueParsers.FormatSize(SizeBytes));

    public string WastedText => L.F("{0} récupérables", ValueParsers.FormatSize(WastedBytes));

    /// <summary>Icône Segoe selon le type de fichier.</summary>
    public string Glyph => DuplicateFinder.GlyphFor(Files[0].Path);

    /// <summary>
    /// Pré-sélection de la copie à garder : la plus récente, puis le chemin le plus court
    /// (« photo.jpg » plutôt que « photo (1).jpg »).
    /// </summary>
    private void ChooseKeep()
    {
        var keep = Files
            .OrderByDescending(f => f.LastModified)
            .ThenBy(f => f.Path.Length)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .First();
        SetKeep(keep);
    }

    /// <summary>Change la copie conservée (décochée d'office).</summary>
    public void SetKeep(DuplicateFile keep)
    {
        if (!Files.Contains(keep) || keep.Succeeded == true)
            return;
        foreach (var file in Files)
            file.IsKeep = ReferenceEquals(file, keep);
        NotifySelection();
    }

    /// <summary>On peut cocher ce fichier seulement s'il reste au moins une autre copie non cochée et présente.</summary>
    internal bool CanDelete(DuplicateFile file) =>
        Files.Any(f => !ReferenceEquals(f, file) && !f.IsSelected && f.Succeeded != true);

    /// <summary>Coche toutes les copies sauf celle à garder.</summary>
    public void SelectCopies()
    {
        foreach (var file in Files)
        {
            if (!file.IsKeep && file.Succeeded != true)
                file.IsSelected = true;
        }
    }

    public void ClearSelection()
    {
        foreach (var file in Files)
            file.IsSelected = false;
    }

    internal void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(Title));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
