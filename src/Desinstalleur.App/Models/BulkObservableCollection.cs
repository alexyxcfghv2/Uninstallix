using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Desinstalleur.App.Models;

/// <summary>
/// Collection observable qui remplace tout son contenu en une seule notification
/// (la liste à l'écran n'est triée et filtrée qu'une fois, au lieu d'une fois par élément).
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
