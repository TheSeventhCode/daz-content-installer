using System;
using System.Collections.Generic;
using System.Linq;

namespace DazContentInstaller.ViewModels;

public static class ArchiveFileGroupBuilder
{
    public static IReadOnlyList<ArchiveFileGroupViewModel> Build(IReadOnlyList<ArchiveFileGroupViewModel> groups,
        Guid rootArchiveId, string rootArchiveName, string? rootDisplayName)
    {
        if (groups.Count == 0)
            return groups;

        var subArchiveCount = groups.Count(x => x.IsSubArchive);
        if (subArchiveCount != 1)
            return groups;

        var flatGroup = new ArchiveFileGroupViewModel
        {
            ArchiveId = rootArchiveId,
            ArchiveName = rootArchiveName,
            DisplayName = rootDisplayName,
            IsSubArchive = false,
            ShowGroupHeader = false,
            IsExpanded = true
        };

        foreach (var file in groups
                     .SelectMany(x => x.Files)
                     .OrderBy(x => x.InstalledRelativePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.InstalledAt))
        {
            flatGroup.Files.Add(file);
        }

        return [flatGroup];
    }
}
