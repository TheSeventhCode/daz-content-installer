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
            .HasIndex(x => new { x.AssetLibraryId, x.ArchiveName, x.ParentArchiveId });

        modelBuilder.Entity<Archive>()
            .HasIndex(x => new { x.AssetLibraryId, x.ParentArchiveId, x.ContentFingerprint });

        modelBuilder.Entity<Archive>()
            .HasMany(x => x.SubArchives)
            .WithOne(x => x.ParentArchive)
            .HasForeignKey(x => x.ParentArchiveId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AssetFile>()
            .HasIndex(x => new { x.ArchiveId, x.ArchiveRelativePath })
            .IsUnique();

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

        modelBuilder.Entity<InstallRecord>()
            .HasIndex(x => new { x.ArchiveId, x.InstalledFileId, x.HasBeenOverriden });

        modelBuilder.Entity<InstallFileOperation>()
            .HasOne(x => x.Archive)
            .WithMany()
            .HasForeignKey(x => x.ArchiveId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstallFileOperation>()
            .HasOne(x => x.InstallRecord)
            .WithMany()
            .HasForeignKey(x => x.InstallRecordId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstallFileOperation>()
            .HasOne(x => x.InstalledFile)
            .WithMany()
            .HasForeignKey(x => x.InstalledFileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InstallFileOperation>()
            .HasIndex(x => x.ArchiveId);

        modelBuilder.Entity<InstallFileOperation>()
            .HasIndex(x => x.InstallRecordId);

        modelBuilder.Entity<InstallFileOperation>()
            .HasIndex(x => x.Status);

        modelBuilder.Entity<ArchiveOverride>()
            .HasOne(x => x.RootArchive)
            .WithMany()
            .HasForeignKey(x => x.RootArchiveId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ArchiveOverride>()
            .HasOne(x => x.AssetLibrary)
            .WithMany()
            .HasForeignKey(x => x.AssetLibraryId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ArchiveOverride>()
            .HasIndex(x => new { x.RootArchiveId, x.InstalledRelativeDirectory, x.FileName })
            .IsUnique();

        base.OnModelCreating(modelBuilder);
    }

    public DbSet<AssetLibrary> AssetLibraries { get; set; }
    public DbSet<Archive> Archives { get; set; }
    public DbSet<AssetFile> AssetFiles { get; set; }
    public DbSet<InstalledFile> InstalledFiles { get; set; }
    public DbSet<InstallRecord> InstallRecords { get; set; }
    public DbSet<InstallFileOperation> InstallFileOperations { get; set; }
    public DbSet<ArchiveOverride> ArchiveOverrides { get; set; }
}