using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.Database;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssetLibrary>()
            .HasMany(x => x.Archives)
            .WithOne(x => x.AssetLibrary)
            .HasForeignKey(x => x.AssetLibraryId)
            .IsRequired();

        modelBuilder.Entity<AssetLibrary>()
            .HasMany(x => x.InstalledFiles)
            .WithOne(x => x.AssetLibrary)
            .HasForeignKey(x => x.AssetLibraryId)
            .IsRequired();

        modelBuilder.Entity<Archive>()
            .HasMany(x => x.AssetFiles)
            .WithOne(x => x.Archive)
            .HasForeignKey(x => x.ArchiveId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Archive>()
            .HasMany(x => x.SubArchives)
            .WithOne(x => x.ParentArchive)
            .HasForeignKey(x => x.ParentArchiveId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstalledFile>()
            .HasMany(x => x.InstallRecords)
            .WithOne(x => x.InstalledFile)
            .HasForeignKey(x => x.InstalledFileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstalledFile>()
            .HasIndex(x => new { x.AssetLibraryId, x.InstalledPath, x.FileName })
            .IsUnique();

        modelBuilder.Entity<InstallRecord>()
            .HasOne(x => x.AssetFile)
            .WithOne(x => x.InstallRecord)
            .HasForeignKey<InstallRecord>(x => x.AssetFileId)
            .IsRequired();

        base.OnModelCreating(modelBuilder);
    }

    public DbSet<AssetLibrary> AssetLibraries { get; set; }
    public DbSet<Archive> Archives { get; set; }
    public DbSet<AssetFile> AssetFiles { get; set; }
    public DbSet<InstalledFile> InstalledFiles { get; set; }
    public DbSet<InstallRecord> InstallRecords { get; set; }
}