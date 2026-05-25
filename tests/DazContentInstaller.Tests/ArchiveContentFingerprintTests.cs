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
    public void Compute_IsOrderIndependent()
    {
        var firstOrder = new[]
        {
            (InstalledRelativePath: "Runtime/a.txt", FileHash: "AAA", FileSize: 1UL),
            (InstalledRelativePath: "data/b.txt", FileHash: "BBB", FileSize: 2UL)
        };
        var secondOrder = new[]
        {
            (InstalledRelativePath: "data/b.txt", FileHash: "BBB", FileSize: 2UL),
            (InstalledRelativePath: "Runtime/a.txt", FileHash: "AAA", FileSize: 1UL)
        };

        ArchiveContentFingerprint.Compute(firstOrder)
            .ShouldBe(ArchiveContentFingerprint.Compute(secondOrder));
    }

    [Fact]
    public async Task HashFileAsync_ReturnsStableSha256Hex()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"fingerprint-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(filePath, "hello");
        try
        {
            var hash = await ArchiveContentFingerprint.HashFileAsync(filePath);

            hash.ShouldBe(await ArchiveContentFingerprint.HashFileAsync(filePath));
            hash.Length.ShouldBe(64);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}