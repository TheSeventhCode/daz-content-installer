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
        var root = new TreeNode(archive.ArchiveName, archive.Id, null);
        Add(root);

        foreach (var assetFile in archive.AssetFiles)
        {
            var parts = assetFile.FileName.Split(Path.DirectorySeparatorChar);
            var current = root;
            for (var index = 0; index < parts.Length; index++) 
                current = current.GetOrAddChild(parts[index], index == parts.Length - 1 ? assetFile.Id : null);
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