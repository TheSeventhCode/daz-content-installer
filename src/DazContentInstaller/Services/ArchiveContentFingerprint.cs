using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DazContentInstaller.Services;

public static class ArchiveContentFingerprint
{
    public static string Compute(IEnumerable<(string InstalledRelativePath, string FileHash, ulong FileSize)> files)
    {
        var lines = files
            .OrderBy(x => x.InstalledRelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.InstalledRelativePath}|{x.FileHash}|{x.FileSize}");
        return HashString(string.Join('\n', lines));
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string HashString(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}
