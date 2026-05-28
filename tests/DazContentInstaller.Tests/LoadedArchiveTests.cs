using DazContentInstaller.Database;
using DazContentInstaller.Models;
using Shouldly;

namespace DazContentInstaller.Tests;

public class LoadedArchiveTests
{
    [Fact]
    public void InstallCompleted_IsFalseUntilExplicitlyMarked()
    {
        var archive = new LoadedArchive("/tmp/example.zip");

        archive.InstallCompleted.ShouldBeFalse();
        archive.MarkInstallCompleted();
        archive.InstallCompleted.ShouldBeTrue();
        archive.ResetInstallCompletion();
        archive.InstallCompleted.ShouldBeFalse();
    }

    [Fact]
    public void SetInstallProgressSilently_ClearsInstallCompleted()
    {
        var archive = new LoadedArchive("/tmp/example.zip");
        archive.MarkInstallCompleted();

        archive.SetInstallProgressSilently(
            ArchiveStatus.Installing,
            "Installing",
            errorMessage: null,
            currentFile: "content/foo.duf",
            processedFiles: 1,
            totalFiles: 10,
            progressPercent: 10);

        archive.InstallCompleted.ShouldBeFalse();
        archive.ArchiveStatus.ShouldBe(ArchiveStatus.Installing);
    }

}
