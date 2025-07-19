using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using DazContentInstaller.Database;

namespace DazContentInstaller.Models;

public class InstalledArchiveTree : ObservableCollection<TreeNode>
{
    public TreeNode LoadArchive(Archive archive)
    {
        var root = new TreeNode(archive.ArchiveName, archive.Id, null, isLazyLoad: false);
        Add(root);

        foreach (var assetFile in archive.AssetFiles)
        {
            var parts = assetFile.FileName.Split(Path.DirectorySeparatorChar);
            var current = root;
            for (var index = 0; index < parts.Length; index++) 
                current = current.GetOrAddChild(parts[index], index == parts.Length - 1 ? assetFile.Id : null);
        }

        SortTree(root);
        root.MarkChildrenLoaded();
        return root;
    }
    
    public TreeNode LoadArchiveLazy(Archive archive)
    {
        var root = new TreeNode(archive.ArchiveName, archive.Id, null, isLazyLoad: true);
        Add(root);
        return root;
    }
    
    public static void LoadArchiveFiles(TreeNode archiveNode, Archive archive)
    {
        if (!archiveNode.IsLazyLoad || archiveNode.HasLoadedChildren)
            return;

        archiveNode.ClearPlaceholders();

        foreach (var assetFile in archive.AssetFiles)
        {
            var parts = assetFile.FileName.Split(Path.DirectorySeparatorChar);
            var current = archiveNode;
            for (var index = 0; index < parts.Length; index++) 
                current = current.GetOrAddChild(parts[index], index == parts.Length - 1 ? assetFile.Id : null);
        }

        SortTree(archiveNode);
        archiveNode.MarkChildrenLoaded();
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