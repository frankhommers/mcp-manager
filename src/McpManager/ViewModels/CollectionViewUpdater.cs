using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace McpManager.ViewModels;

internal static class CollectionViewUpdater
{
  public static void Update<T>(ObservableCollection<T> view, IEnumerable<T> items)
  {
    List<T> desired = items.ToList();
    HashSet<T> included = desired.ToHashSet();
    for (int index = view.Count - 1; index >= 0; index--)
    {
      if (!included.Contains(view[index]))
      {
        view.RemoveAt(index);
      }
    }

    for (int index = 0; index < desired.Count; index++)
    {
      int currentIndex = view.IndexOf(desired[index]);
      if (currentIndex < 0)
      {
        view.Insert(index, desired[index]);
      }
      else if (currentIndex != index)
      {
        view.Move(currentIndex, index);
      }
    }
  }
}
