using KRemote.ViewModels;

namespace KRemote.Views;

public partial class SharePage : ContentPage
{
    public SharePage(ShareViewModel model)
    {
        InitializeComponent();
        BindingContext = model;
    }
}
