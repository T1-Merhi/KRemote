using KRemote.Services;

namespace KRemote;

public partial class App : Application
{
    private readonly InboxService _inbox;

    public App(InboxService inbox)
    {
        InitializeComponent();

        _inbox = inbox;
        _inbox.LoadSaved();
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
        window.Resumed += (_, _) => _inbox.StartListening();
        window.Stopped += (_, _) => _inbox.StopListening();
#else
        _inbox.StartListening();
        window.Destroying += (_, _) => _inbox.StopListening();
#endif

        return window;
    }
}
