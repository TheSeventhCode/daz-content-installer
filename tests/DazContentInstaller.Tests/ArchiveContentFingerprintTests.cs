using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveContentFingerprintTests
{
    [Fact]
    public void Compute_ChangesWhenManifestContentChanges()
    {
        var original = ArchiveContentFingerprint.Compute([
            (InstalledRelativePath: "data/a/file.txt", FileHash: "ABC123", FileSize: 10UL)
        ]);
        var changed = ArchiveContentFingerprint.Compute([
            (InstalledRelativePath: "data/a/file.txt", FileHash: "DEF456", FileSize: 10UL)
        ]);

        changed.ShouldNotBe(original);
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

            hash.ShouldBe("2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}