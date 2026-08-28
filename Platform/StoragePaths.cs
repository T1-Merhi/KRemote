using System.IO;

namespace KRemote.Platform;

public sealed class StoragePaths : IStoragePaths
{
    public string AppDataDirectory
    {
        get
        {
#if ANDROID
            return FileSystem.AppDataDirectory;
#else
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRemote");
#endif
        }
    }

    public string DefaultReceivedFilesDirectory
    {
        get
        {
#if ANDROID
            return Path.Combine(FileSystem.AppDataDirectory, "Received");
#else
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            if (!Directory.Exists(downloads))
                downloads = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return Path.Combine(downloads, "KRemote");
#endif
        }
    }

    public bool SupportsFolderChoice
    {
#if ANDROID
        get => false;
#else
        get => true;
#endif
    }
}
