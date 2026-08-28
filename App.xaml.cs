using KRemote.Services;

namespace KRemote;

public partial class App : Application
{
    private readonly SessionService _session;

    public App(SessionService session)
    {
        InitializeComponent();

        UserAppTheme = AppTheme.Light;

        _session = session;
        _session.LoadSaved();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Title = "KRemote",
            Width = 1100,
            Height = 720,
            MinimumWidth = 420,
            MinimumHeight = 520
        };

#if ANDROID
        window.Resumed += (_, _) => _session.StartListening();
        window.Stopped += (_, _) => _session.StopListening();
#else
        _session.StartListening();
        window.Destroying += (_, _) => _session.StopListening();
#endif

#if WINDOWS
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
                Platforms.Windows.TitleBarStyling.Apply(native);
        };
#endif

        return window;
    }
}
