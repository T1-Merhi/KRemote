using System.Windows;
using System.Windows.Media;

namespace KRemote.Views;

public partial class ToastWindow : Window
{
    private const string CheckGlyph = "";
    private const string ErrorGlyph = "";

    public ToastWindow(string message, bool success)
    {
        InitializeComponent();

        if (success)
        {
            HeadlineText.Text = "Sent";
            IconGlyph.Text = CheckGlyph;
            IconGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x82, 0x45));
            IconCircle.Background = new SolidColorBrush(Color.FromRgb(0xE7, 0xF7, 0xEE));
        }
        else
        {
            HeadlineText.Text = "Not sent";
            IconGlyph.Text = ErrorGlyph;
            IconGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
            IconCircle.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xEC, 0xEA));
        }

        DetailText.Text = message;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
