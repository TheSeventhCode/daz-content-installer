using System;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Models;

public class AssetLibraryModel : ViewModelBase
{
    private string _name;
    private string _path;
    private bool _isDefault;
    private DateTime _createdDate;
    private Guid _id;

    public AssetLibraryModel(Guid id, string name, string path, bool isDefault, DateTime createdDate)
    {
        _name = name;
        _path = path;
        _isDefault = isDefault;
        _createdDate = createdDate;
        _id = id;
    }

    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set => SetProperty(ref _isDefault, value);
    }

    public DateTime CreatedDate
    {
        get => _createdDate;
        set => SetProperty(ref _createdDate, value);
    }
}