using System.Windows;
using KRemote.Models;

namespace KRemote.Views;

public partial class FirstRunPinPrompt : Window
{
    public FirstRunPinPrompt()
    {
        InitializeComponent();
    }

    private void EnableCheck_Changed(object sender, RoutedEventArgs e)
    {
        OptionsPanel.Visibility = EnableCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Apply(AppSettings settings)
    {
        settings.PinEnabled = EnableCheck.IsChecked == true;
        if (settings.PinEnabled)
        {
            settings.PinMode = RandomRadio.IsChecked == true ? PinMode.RandomEachLaunch : PinMode.Permanent;
            settings.Pin = PinBox.Text.Trim();
        }
        settings.FirstRunPromptShown = true;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (EnableCheck.IsChecked == true && PermanentRadio.IsChecked == true &&
            (PinBox.Text.Trim().Length != 4 || !PinBox.Text.Trim().All(char.IsDigit)))
        {
            MessageBox.Show(this, "Enter a 4-digit PIN.", "KRemote", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
