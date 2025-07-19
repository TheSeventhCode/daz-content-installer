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
    
    public bool IsLazyLoad { get; set; }
    public bool IsExpanded { get; set; }
    public bool HasLoadedChildren { get; set; }

    public TreeNode(string title, Guid? id, TreeNode? parent, bool isLazyLoad = false)
    {
        Title = title;
        Parent = parent;
        DbId = id;
        IsLazyLoad = isLazyLoad;
        HasLoadedChildren = !isLazyLoad;
        
        if (isLazyLoad) 
            Children.Add(new TreeNode("Loading...", null, this));
    }

    public TreeNode(string title, Guid? dbId, IEnumerable<TreeNode> children, TreeNode? parent) : this(title, dbId, parent)
    {
        Children.AddRange(children);
    }

    public TreeNode GetOrAddChild(string name, Guid? dbId)
    {
        var child = Children.FirstOrDefault(c => c.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (child is not null) return child;
        
        child = new TreeNode(name, dbId, this);
        Children.Add(child);

        return child;
    }
    
    public void ClearPlaceholders()
    {
        if (IsLazyLoad && !HasLoadedChildren)
        {
            Children.Clear();
        }
    }
    
    public void MarkChildrenLoaded()
    {
        HasLoadedChildren = true;
    }
}