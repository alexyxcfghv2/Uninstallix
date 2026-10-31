using Desinstalleur.App.Services;

namespace Desinstalleur.App.Models;

/// <summary>Conseil pour une application Windows préinstallée (ordre d'affichage).</summary>
public enum WindowsAppAdvice
{
    /// <summary>« Sans risque » : publicité, jeux ou applications que peu de gens utilisent.</summary>
    Safe = 0,
    /// <summary>« À savoir » : supprimable, mais utile à certains (Xbox, Photos, Courrier…).</summary>
    Know = 1,
    /// <summary>« À vérifier » : application inconnue de la liste intégrée.</summary>
    Check = 2,
    /// <summary>« À garder » : utile au fonctionnement de Windows ou d'autres applications.</summary>
    Keep = 3,
}

/// <summary>Une application Windows (paquet Appx / MSIX) installée pour l'utilisateur actuel. Jamais cochée d'office.</summary>
public sealed class WindowsApp : ObservableObject
{
    private bool _isSelected;
    private string? _resultText;
    private bool? _succeeded;

    /// <summary>Nom du paquet (« Microsoft.BingNews »).</summary>
    public required string Name { get; init; }
    /// <summary>Noms complets des paquets à supprimer (souvent un seul ; parfois une version par architecture).</summary>
    public required IReadOnlyList<string> PackageFullNames { get; init; }
    /// <summary>Nom affiché, en français (traduit à l'affichage).</summary>
    public required string FrenchName { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string? InstallLocation { get; init; }
    public bool IsMicrosoft { get; init; }
    public required WindowsAppAdvice Advice { get; init; }
    /// <summary>Explication du conseil, en français (traduite à l'affichage).</summary>
    public string? FrenchNote { get; init; }
    /// <summary>Vrai pour les rares applications qu'Uninstallix refuse de supprimer (Store, App Installer…).</summary>
    public bool Locked { get; init; }

    public string DisplayName => L.T(FrenchName);
    public string NoteText => string.IsNullOrEmpty(FrenchNote) ? string.Empty : L.T(FrenchNote);
    public bool HasNote => !string.IsNullOrEmpty(FrenchNote);
    public string PublisherText => string.IsNullOrWhiteSpace(Publisher) ? L.T("Éditeur inconnu") : Publisher!;
    public string VersionText => string.IsNullOrWhiteSpace(Version) ? string.Empty : L.F("Version {0}", Version);
    public bool CanSelect => !Locked && _succeeded != true;
    public string LockedText => L.T("Indispensable : Uninstallix ne la supprime pas.");

    public string AdviceText => Advice switch
    {
        WindowsAppAdvice.Safe => L.T("Sans risque"),
        WindowsAppAdvice.Know => L.T("À savoir"),
        WindowsAppAdvice.Keep => L.T("À garder"),
        _ => L.T("À vérifier"),
    };

    public string SearchText => $"{DisplayName} {FrenchName} {Name} {Publisher} {AdviceText}";

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value && CanSelect);
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
            if (!Set(ref _succeeded, value))
                return;
            if (value == true)
                IsSelected = false;
            OnPropertyChanged(nameof(CanSelect));
        }
    }
}
