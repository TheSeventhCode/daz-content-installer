using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData;

namespace DazContentInstaller.Models;

public class TreeNode
{
    public ObservableCollection<TreeNode> Children { get; set; } = [];
    public string Title { get; set; }

    public TreeNode(string title)
    {
        Title = title;
    }

    public TreeNode(string title, IEnumerable<TreeNode> children)
    {
        Title = title;
        Children.AddRange(children);
    }

    public TreeNode GetOrAddChild(string name)
    {
        var child = Children.FirstOrDefault(c => c.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (child is null)
        {
            child = new TreeNode(name);
            Children.Add(child);
        }

        return child;
    }
}