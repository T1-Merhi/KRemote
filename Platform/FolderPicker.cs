namespace KRemote.Platform;

public sealed class FolderPicker : IFolderPicker
{
    public async Task<string?> PickAsync(string? startingFolder)
    {
#if WINDOWS
        var picker = new global::Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.Downloads
        };
        picker.FileTypeFilter.Add("*");

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(native);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
