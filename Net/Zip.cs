using System.IO;
using System.IO.Compression;

namespace KRemote.Net;

/// <summary>Bundles several staged files into one temporary archive for sending as a single file.</summary>
internal static class Zip
{
    public static string CreateTempArchive(IReadOnlyList<string> filePaths, string? title)
    {
        var name = FileNaming.Sanitize(title, $"files-{DateTime.Now:yyyyMMdd-HHmmss}");
        name = Path.GetFileNameWithoutExtension(name);
        var path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.zip");

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in filePaths)
            archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);

        return path;
    }
}
