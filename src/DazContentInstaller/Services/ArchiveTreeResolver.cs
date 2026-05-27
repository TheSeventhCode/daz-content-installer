using System;
using System.Collections.Generic;
using System.Linq;

namespace DazContentInstaller.Services;

public static class ArchiveTreeResolver
{
    public static HashSet<Guid> GetTreeIds(IReadOnlyDictionary<Guid, Guid?> parentById, Guid rootId)
    {
        var childrenByParent = parentById
            .Where(x => x.Value is not null)
            .GroupBy(x => x.Value!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToList());

        var treeIds = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!treeIds.Add(current))
                continue;

            if (!childrenByParent.TryGetValue(current, out var children))
                continue;
            
            foreach (var child in children)
                queue.Enqueue(child);
        }

        return treeIds;
    }

    public static InstallRecordCounts RollUpCounts(
        IEnumerable<Guid> archiveIds,
        IReadOnlyDictionary<Guid, InstallRecordCounts> countsByArchiveId)
    {
        var totals = InstallRecordCounts.Empty;
        foreach (var archiveId in archiveIds)
        {
            if (countsByArchiveId.TryGetValue(archiveId, out var counts))
                totals = totals.Add(counts);
        }

        return totals;
    }
}