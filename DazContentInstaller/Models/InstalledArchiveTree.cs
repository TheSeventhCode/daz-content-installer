using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using DazContentInstaller.Database;

namespace DazContentInstaller.Models;

public class InstalledArchiveTree : ObservableCollection<TreeNode>
{
    public void LoadArchive(Archive archive)
    {
        var root = new TreeNode(archive.ArchiveName);
        Add(root);

        foreach (var path in archive.AssetFiles.Select(d => d.FileName))
        {
            var parts = path.Split(Path.DirectorySeparatorChar);
            var current = root;
            foreach (var t in parts)
                current = current.GetOrAddChild(t);
        }

        SortTree(root);
    }

    private static void SortTree(TreeNode node)
    {
        var sorted = node.Children.OrderBy(child => child.Title).ToList();
        node.Children.Clear();
        foreach (var child in sorted)
            node.Children.Add(child);

        foreach (var child in node.Children)
            SortTree(child);
    }
}