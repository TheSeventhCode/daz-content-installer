using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.Database;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base( options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssetLibrary>()
            .HasMany(x => x.Archives)
            .WithOne(x => x.AssetLibrary)
            .HasForeignKey(x => x.AssetLibraryId)
            .IsRequired();
        
        modelBuilder.Entity<Archive>()
            .HasMany(x => x.AssetFiles)
            .WithOne(x => x.Archive)
            .HasForeignKey(x => x.ArchiveId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        
        base.OnModelCreating(modelBuilder);
    }

    public DbSet<AssetLibrary> AssetLibraries { get; set; }
    public DbSet<Archive> Archives { get; set; }
    public DbSet<AssetFile> AssetFiles { get; set; }
}