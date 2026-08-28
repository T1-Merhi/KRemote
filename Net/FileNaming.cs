using System.IO;

namespace KRemote.Net;

internal static class FileNaming
{
    public static string Sanitize(string? requested, string fallback = "file")
    {
        var name = requested ?? "";

        var cut = name.LastIndexOfAny(['/', '\\', ':']);
        if (cut >= 0) name = name[(cut + 1)..];
        name = Path.GetFileName(name);

        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        name = name.Trim().TrimEnd('.');

        if (name.Length == 0 || name is "." or "..")
            name = fallback;

        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                             "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
                             "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase))
            name = "_" + name;

        if (name.Length > 200) name = name[..200];

        return name;
    }

    public static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path) && !File.Exists(path + ".part")) return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; index < int.MaxValue; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".part")) return candidate;
        }

        throw new IOException($"Could not find a free name for {fileName}.");
    }
}
