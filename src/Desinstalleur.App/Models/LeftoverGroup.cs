namespace Desinstalleur.App.Models;

/// <summary>Les restes d'un même logiciel, affichés ensemble dans une carte.</summary>
public sealed class LeftoverGroup
{
    public LeftoverGroup(string programName, IReadOnlyList<LeftoverItem> items)
    {
        ProgramName = programName;
        Items = items;
    }

    public string ProgramName { get; }
    public IReadOnlyList<LeftoverItem> Items { get; }

    public string CountText => Items.Count == 1 ? "1 élément" : $"{Items.Count} éléments";
}
