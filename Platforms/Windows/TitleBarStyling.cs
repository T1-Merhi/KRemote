namespace KRemote.Platforms.Windows;

internal static class TitleBarStyling
{
    public static void Apply(Microsoft.UI.Xaml.Window window)
    {
        if (window.Content is Microsoft.UI.Xaml.FrameworkElement root)
            root.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Light;
    }
}
