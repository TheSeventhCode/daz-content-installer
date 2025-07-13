using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData;

namespace DazContentInstaller.Models;

public class TreeNode
{
    public ObservableCollection<TreeNode> Children { get; set; } = [];
    public TreeNode? Parent { get; }
    public string Title { get; }
    public Guid? DbId { get; }

    public TreeNode(string title, Guid? id, TreeNode? parent)
    {
        Title = title;
        Parent = parent;
        DbId = id;
    }

    public TreeNode(string title, Guid? dbId, IEnumerable<TreeNode> children, TreeNode? parent) : this(title, dbId, parent)
    {
        Children.AddRange(children);
    }

    public TreeNode GetOrAddChild(string name, Guid? dbId)
    {
        var child = Children.FirstOrDefault(c => c.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (child is null)
        {
            child = new TreeNode(name, dbId, this);
            Children.Add(child);
        }

        return child;
    }
}