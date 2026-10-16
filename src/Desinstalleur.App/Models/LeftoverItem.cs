using Desinstalleur.App.Services;
using Desinstalleur.Core;
using Microsoft.Win32;

namespace Desinstalleur.App.Models;

public enum LeftoverKind
{
    Folder,
    File,
    RegistryKey,
}

/// <summary>Fichier, dossier ou clé de registre resté après une désinstallation.</summary>
public sealed class LeftoverItem : ObservableObject
{
    private bool _isSelected;
    private string? _resultText;
    private bool? _succeeded;

    public required string ProgramName { get; init; }
    public required LeftoverKind Kind { get; init; }

    /// <summary>Chemin du fichier ou du dossier, ou chemin complet de la clé (« HKEY_… »).</summary>
    public required string Path { get; init; }

    /// <summary>Pour une clé : ruche et sous-clé ouvertes en vue 64 bits.</summary>
    public RegistryHive Hive { get; init; }
    public string? RegistrySubKey { get; init; }

    public long? SizeBytes { get; init; }
    public required MatchConfidence Confidence { get; init; }
    public required string Reason { get; init; }

    public string KindText => Kind switch
    {
        LeftoverKind.Folder => L.T("Dossier"),
        LeftoverKind.File => L.T("Fichier"),
        _ => L.T("Registre"),
    };

    /// <summary>Glyphe de la police Segoe Fluent Icons / MDL2 Assets.</summary>
    public string KindGlyph => Kind switch
    {
        LeftoverKind.Folder => "",
        LeftoverKind.File => "",
        _ => "",
    };

    public string SizeText => Kind == LeftoverKind.RegistryKey ? string.Empty : ValueParsers.FormatSize(SizeBytes);

    public string ConfidenceText => Confidence == MatchConfidence.High ? L.T("Sûr") : L.T("À vérifier");

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string? ResultText
    {
        get => _resultText;
        set => Set(ref _resultText, value);
    }

    public bool? Succeeded
    {
        get => _succeeded;
        set => Set(ref _succeeded, value);
    }
}
