namespace KRemote.Platform;

public interface IStoragePaths
{
    string AppDataDirectory { get; }

    string DefaultReceivedFilesDirectory { get; }

    bool SupportsFolderChoice { get; }
}
