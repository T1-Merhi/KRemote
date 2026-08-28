namespace KRemote.Platform;

public interface IFolderPicker
{
    Task<string?> PickAsync(string? startingFolder);
}
