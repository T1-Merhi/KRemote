namespace KRemote.Platform;

public interface IFileActions
{
    bool SupportsReveal { get; }

    bool SupportsShare { get; }

    Task OpenAsync(string filePath);

    Task RevealAsync(string filePath);

    Task ShareAsync(string filePath);
}
