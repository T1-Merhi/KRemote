using KRemote.Views;

namespace KRemote;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("share", typeof(SharePage));
        Routing.RegisterRoute("firstrunpin", typeof(FirstRunPinPage));
    }
}
