using System.ComponentModel;
using KRemote.ViewModels;

namespace KRemote.Views;

public partial class SavedPage : ContentPage
{
    private readonly SavedViewModel _model;

    public SavedPage(SavedViewModel model)
    {
        InitializeComponent();

        _model = model;
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _model.Refresh();
    }
}
