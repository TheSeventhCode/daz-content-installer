using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class AssetLibrary
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    [StringLength(256)]
    public string Name { get; set; } = null!;
    [StringLength(4096)]
    public string Path { get; set; } = null!;

    public bool IsDefault { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime LastUsed { get; set; } = DateTime.Now;
    public List<Archive> Archives { get; set; } = [];
}