using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace DazContentInstaller.Extensions;

public static class ObservableCollectionExtensions
{
    public static void SortBy<T>(this ObservableCollection<T> collection, Func<T, string> keySelector)
    {
        var sorted = collection.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase).ToList();
        collection.Clear();
        foreach (var item in sorted)
            collection.Add(item);
    }
}
