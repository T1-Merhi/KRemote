using System.ComponentModel;
using KRemote.Services;
using KRemote.ViewModels;

namespace KRemote.Views;

public partial class InboxPage : ContentPage
{
    private readonly InboxViewModel _model;
    private readonly SettingsService _settings;
    private bool _firstRunChecked;

    public InboxPage(InboxViewModel model, SettingsService settings)
    {
        InitializeComponent();

        _model = model;
        _settings = settings;
        BindingContext = model;

        _model.PropertyChanged += OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MessageListViewModel.ShowList)
            or nameof(MessageListViewModel.ShowDetail)
            or nameof(MessageListViewModel.IsWide))
        {
            AdaptiveLayout.Apply(BodyGrid, _model);
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0) return;

        _model.IsWide = width >= AdaptiveLayout.WideThreshold;
        AdaptiveLayout.Apply(BodyGrid, _model);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _model.Refresh();

        if (_firstRunChecked) return;
        _firstRunChecked = true;

        if (_settings.Current.FirstRunPromptShown) return;

        await Shell.Current.GoToAsync("firstrunpin");
    }
}
