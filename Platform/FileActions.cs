using System.IO;

namespace KRemote.Platform;

public sealed class FileActions : IFileActions
{
    public bool SupportsReveal
    {
#if ANDROID
        get => false;
#else
        get => true;
#endif
    }

    public bool SupportsShare
    {
#if ANDROID
        get => true;
#else
        get => false;
#endif
    }

    public async Task OpenAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("That file is no longer at the saved path.", filePath);

        await Launcher.Default.OpenAsync(new OpenFileRequest
        {
            File = new ReadOnlyFile(filePath)
        });
    }

    public Task RevealAsync(string filePath)
    {
#if WINDOWS
        var folder = Path.GetDirectoryName(filePath);

        if (File.Exists(filePath))
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\""));
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(folder))
            throw new DirectoryNotFoundException("That file has no folder to open.");

        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        return Task.CompletedTask;
#else
        return Task.CompletedTask;
#endif
    }

    public async Task ShareAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("That file is no longer at the saved path.", filePath);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = Path.GetFileName(filePath),
            File = new ShareFile(filePath)
        });
    }
}
