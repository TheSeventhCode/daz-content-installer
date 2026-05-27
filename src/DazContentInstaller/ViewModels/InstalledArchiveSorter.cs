using System;
using System.Collections.Generic;
using System.Linq;

namespace DazContentInstaller.ViewModels;

public static class InstalledArchiveSorter
{
    public static IOrderedEnumerable<InstalledArchiveViewModel> Sort(
        IEnumerable<InstalledArchiveViewModel> archives,
        InstalledArchiveSortMode sortMode)
    {
        return sortMode switch
        {
            InstalledArchiveSortMode.NameDescending => archives
                .OrderByDescending(x => x.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(x => x.ArchiveName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id),
            InstalledArchiveSortMode.InstalledNewest => archives
                .OrderBy(x => x.InstalledAt is null)
                .ThenByDescending(x => x.InstalledAt)
                .ThenBy(x => x.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id),
            InstalledArchiveSortMode.InstalledOldest => archives
                .OrderBy(x => x.InstalledAt is null)
                .ThenBy(x => x.InstalledAt)
                .ThenBy(x => x.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id),
            _ => archives
                .OrderBy(x => x.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ArchiveName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)
        };
    }
}
