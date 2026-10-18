using System.Windows.Media;
using Desinstalleur.App.Services;
using Microsoft.Win32;

namespace Desinstalleur.App.Models;

/// <summary>D'où vient une entrée de démarrage.</summary>
public enum StartupSourceKind
{
    /// <summary>Valeur d'une clé « Run » du registre.</summary>
    Registry,
    /// <summary>Fichier (souvent un raccourci) dans un dossier « Démarrage ».</summary>
    Folder,
}

/// <summary>Une application lancée avec Windows (clé Run ou dossier Démarrage).</summary>
public sealed class StartupEntry : ObservableObject
{
    private bool _isEnabled;
    private ImageSource? _icon;

    public required StartupSourceKind Kind { get; init; }
    /// <summary>Ruche du registre : HKCU (pour vous) ou HKLM (tous les utilisateurs).</summary>
    public required RegistryHive Hive { get; init; }
    /// <summary>Clé Run (vue 64 bits) pour une entrée de registre, dossier pour un fichier.</summary>
    public required string Location { get; init; }
    /// <summary>Nom de la valeur Run, ou nom du fichier dans le dossier Démarrage.</summary>
    public required string ValueName { get; init; }
    /// <summary>Sous-clé de StartupApproved qui porte l'état (Run, Run32 ou StartupFolder).</summary>
    public required string ApprovedSubKey { get; init; }

    public required string Command { get; init; }
    /// <summary>Exécutable réellement lancé (cible du raccourci, ou premier élément de la commande).</summary>
    public string? ExePath { get; init; }
    /// <summary>Chemin complet du fichier dans le dossier Démarrage.</summary>
    public string? FilePath { get; init; }
    public bool FileMissing { get; init; }

    public required string DisplayName { get; init; }
    public string? Publisher { get; init; }

    public bool IsAllUsers => Hive == RegistryHive.LocalMachine;
    public string ScopeText => IsAllUsers ? L.T("Tous les utilisateurs") : L.T("Pour vous");
    public string SourceText => Kind == StartupSourceKind.Folder
        ? L.T("Dossier Démarrage")
        : ApprovedSubKey == "Run32" ? L.T("Registre (32 bits)") : L.T("Registre");
    public string PublisherText => string.IsNullOrWhiteSpace(Publisher) ? L.T("Éditeur inconnu") : Publisher!;
    public string StateText => IsEnabled ? L.T("Activé") : L.T("Désactivé");

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (Set(ref _isEnabled, value))
                OnPropertyChanged(nameof(StateText));
        }
    }

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

    public string SearchText => $"{DisplayName} {Publisher} {ValueName} {Command}";
}
