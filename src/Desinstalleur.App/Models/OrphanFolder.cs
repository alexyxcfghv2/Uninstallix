using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Models;

/// <summary>Degré de confiance d'un reste oublié (même vocabulaire que l'écran des restes).</summary>
public enum OrphanConfidence
{
    /// <summary>« À vérifier » : à regarder de près avant de cocher.</summary>
    Check = 0,
    /// <summary>« Probable » : très probablement abandonné.</summary>
    Likely = 1,
    /// <summary>« Sûr » : reste d'un logiciel désinstallé, aucun programme dedans.</summary>
    Safe = 2,
}

/// <summary>Dossier laissé par un logiciel qui n'est plus installé. Jamais coché d'office.</summary>
public sealed class OrphanFolder : ObservableObject
{
    private bool _isSelected;
    private string? _resultText;
    private bool? _succeeded;

    public required string Path { get; init; }
    public required string Name { get; init; }

    /// <summary>Emplacement racine lisible (« Program Files », « AppData\Roaming »…).</summary>
    public required string Location { get; init; }

    public long SizeBytes { get; init; }
    /// <summary>Vrai si le comptage s'est arrêté avant la fin (dossier énorme) : la taille est un minimum.</summary>
    public bool SizeIsPartial { get; init; }
    public DateTime LastModified { get; init; }
    public bool HasPrograms { get; init; }

    public required OrphanConfidence Confidence { get; init; }
    public required string Reason { get; init; }

    public string SizeText => (SizeIsPartial ? "≥ " : string.Empty) + ValueParsers.FormatSize(SizeBytes);

    public string ModifiedText => L.F("Modifié le {0}",
        LastModified.ToString(L.IsEnglish ? "MMM d, yyyy" : "d MMM yyyy", L.Culture));

    public string ConfidenceText => Confidence switch
    {
        OrphanConfidence.Safe => L.T("Sûr"),
        OrphanConfidence.Likely => L.T("Probable"),
        _ => L.T("À vérifier"),
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value && _succeeded != true);
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
        }
    }
}
