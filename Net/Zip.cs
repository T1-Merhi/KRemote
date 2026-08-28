using System.IO;
using System.IO.Compression;

namespace KRemote.Net;

internal static class Zip
{
    public static string CreateTempArchive(IReadOnlyList<string> filePaths, string? title)
    {
        var name = FileNaming.Sanitize(title, $"files-{DateTime.Now:yyyyMMdd-HHmmss}");

        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        var path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.zip");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in filePaths)
            archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);

        return path;
    }
}
