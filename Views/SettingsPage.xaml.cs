using KRemote.ViewModels;

namespace KRemote.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel model)
    {
        InitializeComponent();
        BindingContext = model;
    }
}
