using System.Windows.Media;
using Desinstalleur.App.Services;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Models;

/// <summary>Un logiciel lu dans une clé « Uninstall » du registre.</summary>
public sealed class InstalledProgram : ObservableObject
{
    private bool _isSelected;
    private bool _canSelect = true;
    private long? _sizeBytes;
    private ImageSource? _icon;

    public required string DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string? InstallLocation { get; init; }
    public string? DisplayIconPath { get; init; }
    public required string UninstallString { get; init; }
    /// <summary>Commande sans fenêtre fournie par certains logiciels (QuietUninstallString).</summary>
    public string? QuietUninstallString { get; init; }
    public bool IsSystemComponent { get; init; }
    public bool IsMsi { get; init; }

    /// <summary>Emplacement de la clé dans le registre (vue 64 bits) : ruche + sous-clé.</summary>
    public required RegistryHive Hive { get; init; }
    public required string KeyPath { get; init; }
    public string FullKeyPath => (Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + KeyPath;

    public bool IsPerUser => Hive == RegistryHive.CurrentUser;

    public DateTime? InstallDate { get; init; }
    /// <summary>Vrai si la date vient de la dernière modification de la clé (pas d'InstallDate).</summary>
    public bool InstallDateIsEstimated { get; init; }

    public string? ProtectionReason { get; init; }
    public bool IsProtected => ProtectionReason is not null;

    public long? SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (Set(ref _sizeBytes, value))
                OnPropertyChanged(nameof(SizeText));
        }
    }

    public bool SizeIsEstimated { get; set; }

    public string SizeText => SizeIsEstimated && SizeBytes is not null
        ? "≈ " + ValueParsers.FormatSize(SizeBytes)
        : ValueParsers.FormatSize(SizeBytes);

    /// <summary>Clé de tri : les tailles inconnues vont à la fin.</summary>
    public long SortSize => SizeBytes ?? -1;
    public DateTime SortDate => InstallDate ?? DateTime.MinValue;

    public string InstallDateText => InstallDate is null
        ? "—"
        : (InstallDateIsEstimated ? "≈ " : string.Empty) + InstallDate.Value.ToString(L.IsEnglish ? "MMM d, yyyy" : "d MMM yyyy", L.Culture);

    public string Subtitle => string.Join("  ·  ", new[] { Publisher, Version is null ? null : "v" + Version }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";

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

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value && _canSelect);
    }

    /// <summary>Faux pour un composant protégé tant que l'utilisateur ne l'a pas déverrouillé.</summary>
    public bool CanSelect
    {
        get => _canSelect;
        set
        {
            if (Set(ref _canSelect, value) && !value)
                IsSelected = false;
        }
    }
}
