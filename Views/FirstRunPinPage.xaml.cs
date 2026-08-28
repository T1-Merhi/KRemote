using KRemote.Services;

namespace KRemote.Views;

public partial class FirstRunPinPage : ContentPage
{
    private readonly SettingsService _settings;

    public FirstRunPinPage(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
    }

    private void OnPinToggled(object? sender, ToggledEventArgs e)
    {
        PinPanel.IsVisible = e.Value;
        UpdateHint();
    }

    private void OnPinTextChanged(object? sender, TextChangedEventArgs e) => UpdateHint();

    private bool IsPinValid =>
        PinBox.Text is { Length: 4 } pin && pin.All(char.IsDigit);

    private void UpdateHint()
    {
        if (!PinSwitch.IsToggled || IsPinValid)
        {
            PinHint.Text = "Four digits.";
            PinHint.TextColor = (Color)Application.Current!.Resources["Muted"];
            return;
        }

        PinHint.Text = "Enter 4 digits, or nobody will be able to send to this device.";
        PinHint.TextColor = (Color)Application.Current!.Resources["Danger"];
    }

    private async void OnContinue(object? sender, EventArgs e)
    {
        if (PinSwitch.IsToggled && !IsPinValid)
        {
            UpdateHint();
            return;
        }

        _settings.Current.PinEnabled = PinSwitch.IsToggled;
        _settings.Current.Pin = PinSwitch.IsToggled ? PinBox.Text.Trim() : "";
        _settings.Current.FirstRunPromptShown = true;

        try { _settings.Save(); }
        catch (Exception) { }

        await Shell.Current.GoToAsync("..");
    }
}
