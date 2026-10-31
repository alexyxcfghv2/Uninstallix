using Desinstalleur.App.Services;
using Desinstalleur.Core;

namespace Desinstalleur.App.Models;

/// <summary>Type de gros fichier oublié.</summary>
public enum ForgottenKind
{
    Installer = 0,
    Archive = 1,
    DiskImage = 2,
    Large = 3,
}

/// <summary>Fichier oublié dans Téléchargements ou sur le Bureau. Jamais coché d'office.</summary>
public sealed class ForgottenFile : ObservableObject
{
    private bool _isSelected;
    private string? _resultText;
    private bool? _succeeded;

    public required string Path { get; init; }
    public required ForgottenKind Kind { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastModified { get; init; }
    /// <summary>Dernière utilisation connue (modification, création ou ouverture : la plus récente).</summary>
    public DateTime LastUsed { get; init; }
    /// <summary>« Téléchargements » ou « Bureau » (texte source français).</summary>
    public required string Location { get; init; }

    public string Name => System.IO.Path.GetFileName(Path);
    public string LocationText => L.T(Location);
    public string SizeText => ValueParsers.FormatSize(SizeBytes);

    public string ModifiedText => L.F("Modifié le {0}",
        LastModified.ToString(L.IsEnglish ? "MMM d, yyyy" : "d MMM yyyy", L.Culture));

    public string AgeText
    {
        get
        {
            var days = (int)Math.Max(0, (DateTime.Now - LastUsed).TotalDays);
            if (days >= 730)
                return L.F("Inutilisé depuis {0} ans", days / 365);
            if (days >= 365)
                return L.T("Inutilisé depuis plus d'un an");
            if (days >= 60)
                return L.F("Inutilisé depuis {0} mois", days / 30);
            return days > 1 ? L.F("Inutilisé depuis {0} jours", days) : L.T("Utilisé récemment");
        }
    }

    public string KindText => Kind switch
    {
        ForgottenKind.Installer => L.T("Installeur"),
        ForgottenKind.Archive => L.T("Archive"),
        ForgottenKind.DiskImage => L.T("Image disque"),
        _ => L.T("Gros fichier"),
    };

    public string Glyph => Kind switch
    {
        ForgottenKind.Installer => "",
        ForgottenKind.Archive => "",
        ForgottenKind.DiskImage => "",
        _ => "",
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
