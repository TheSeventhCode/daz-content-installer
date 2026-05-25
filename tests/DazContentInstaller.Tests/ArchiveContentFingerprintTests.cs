using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveContentFingerprintTests
{
    [Fact]
    public void Compute_IsDeterministicForSameManifest()
    {
        var files = new[]
        {
            (InstalledRelativePath: "data/a/file.txt", FileHash: "ABC123", FileSize: 10UL),
            (InstalledRelativePath: "Runtime/textures/image.jpg", FileHash: "DEF456", FileSize: 20UL)
        };

        ArchiveContentFingerprint.Compute(files)
            .ShouldBe(ArchiveContentFingerprint.Compute(files));
    }

    [Fact]
    public void Compute_ChangesWhenManifestChanges()
    {
        var first = new[] { (InstalledRelativePath: "data/a/file.txt", FileHash: "ABC123", FileSize: 10UL) };
        var second = new[] { (InstalledRelativePath: "data/a/file.txt", FileHash: "XYZ789", FileSize: 10UL) };

        ArchiveContentFingerprint.Compute(first)
            .ShouldNotBe(ArchiveContentFingerprint.Compute(second));
    }
}
