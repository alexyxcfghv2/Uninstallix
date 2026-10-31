using Desinstalleur.App.Services;

namespace Desinstalleur.App.Models;

/// <summary>Les restes d'un même logiciel, affichés ensemble dans une carte.</summary>
public sealed class LeftoverGroup
{
    public LeftoverGroup(string programName, IReadOnlyList<LeftoverItem> items, bool isForced = false)
    {
        ProgramName = programName;
        Items = items;
        IsForced = isForced;
    }

    public string ProgramName { get; }
    public IReadOnlyList<LeftoverItem> Items { get; }

    /// <summary>Vrai si ces éléments viennent d'une désinstallation forcée (le logiciel est encore dans la liste).</summary>
    public bool IsForced { get; }

    public string ForcedText => L.T("Désinstallation forcée");

    public string CountText => L.F(Items.Count > 1 ? "{0} éléments" : "{0} élément", Items.Count);
}
