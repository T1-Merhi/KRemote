using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using KRemote.Models;
using KRemote.Net;
using KRemote.Storage;

namespace KRemote;

/// <summary>
/// Every instance is both ends of the link: it listens for text and files from
/// other PCs and can send either to one of them. The window itself is now just
/// three tabs (Inbox, Saved, Settings) plus a floating Share button that opens
/// the compose flow in its own popup.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<InboxMessage> _inbox = [];
    private readonly MessageStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;

    private readonly CollectionViewSource _savedView = new();

    private PeerServer? _server;
    private bool _loadingSettings;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsStore.Load();

        InboxList.ItemsSource = _inbox;

        _savedView.Source = _inbox;
        _savedView.Filter += (_, e) => e.Accepted = e.Item is InboxMessage { IsSaved: true };
        SavedList.ItemsSource = _savedView.View;

        // Saved messages are the only ones that survive a restart; they come
        // back newest first, the same order live arrivals are inserted in.
        foreach (var message in _store.Load().OrderByDescending(m => m.ReceivedAt))
            _inbox.Add(message);

        LoadSettingsIntoUi();

        UpdateInboxStatus();
        UpdateSavedStatus();
        UpdateMessageButtons();
        StartServer();
    }

    // ---------------------------------------------------------------- receiving

    private void StartServer()
    {
        _server = new PeerServer();
        _server.MessageReceived += OnMessageReceived;
        _server.TransferProgress += OnTransferProgress;

        try
        {
            _server.Start();
        }
        catch (SocketException)
        {
            // Almost always a second copy of KRemote on this machine. Sending
            // still works, so keep running instead of failing to start.
        }
    }

    private void OnMessageReceived(InboxMessage message)
    {
        // Raised on a socket thread; the inbox is a UI-bound collection.
        // Arrival is deliberately silent: no popup, no focus steal.
        Dispatcher.Invoke(() =>
        {
            _inbox.Insert(0, message);
            UpdateInboxStatus();
            UpdateSavedStatus();
        });
    }

    private void OnTransferProgress(string fileName, long received, long total)
    {
        // An incoming file is the one case where the inbox is worth narrating,
        // since a large transfer would otherwise look like nothing happening.
        Dispatcher.BeginInvoke(() =>
        {
            if (received >= total)
            {
                UpdateInboxStatus();
                return;
            }

            var percent = total > 0 ? received * 100.0 / total : 0;
            InboxStatus.Text = $"Receiving {fileName}… {percent:0}%  " +
                               $"({InboxMessage.FormatSize(received)} of {InboxMessage.FormatSize(total)})";
        });
    }

    // ---------------------------------------------------------------- tabs

    private void RootTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source != RootTabs) return;
        ShareButton.Visibility = RootTabs.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------- share popup

    private void ShareButton_Click(object sender, RoutedEventArgs e)
    {
        // Populated fully once the Share popup exists; for now this is a stub.
    }

    /// <summary>Shows a small self-dismissing confirmation, e.g. after a successful send.</summary>
    public void ShowToast(string message)
    {
        var toast = new Border
        {
            Background = (Brush)FindResource("Card"),
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = message,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("Ink"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320
            }
        };

        var host = new Window
        {
            Content = toast,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Owner = this,
            Topmost = true
        };

        host.Loaded += (_, _) =>
        {
            host.Left = Left + Width - host.ActualWidth - 24;
            host.Top = Top + Height - host.ActualHeight - 24;
        };

        host.Show();

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            host.Close();
        };
        timer.Start();
    }

    // ---------------------------------------------------------------- inbox / saved

    private void InboxList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        MessageView.Text = InboxList.SelectedItem is InboxMessage message ? Describe(message) : "";
        UpdateMessageButtons();
    }

    private void SavedList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SavedMessageView.Text = SavedList.SelectedItem is InboxMessage message ? Describe(message) : "";
        UpdateMessageButtons();
    }

    /// <summary>Whichever list belongs to the currently selected tab.</summary>
    private System.Windows.Controls.ListBox ActiveList => RootTabs.SelectedIndex == 1 ? SavedList : InboxList;

    private static string Describe(InboxMessage message)
    {
        if (!message.IsFile) return message.Text;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Title)) lines.Add(message.Title.Trim());
        lines.Add(message.FileName);
        lines.Add($"{InboxMessage.FormatSize(message.FileSize)}  ({message.FileSize:N0} bytes)");
        lines.Add($"From {message.From} ({message.FromAddress}) at {message.ReceivedAt:MMM d, yyyy HH:mm:ss}");
        lines.Add("");
        lines.Add("Saved to:");
        lines.Add(message.FilePath);
        return string.Join(Environment.NewLine, lines);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveList.SelectedItem is not InboxMessage { IsFile: true } message) return;

        if (!File.Exists(message.FilePath))
        {
            SetStatus("That file is no longer at the saved path.");
            return;
        }

        try
        {
            // ShellExecute so the file opens in whatever it is associated with.
            Process.Start(new ProcessStartInfo(message.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open the file: {ex.Message}");
        }
    }

    private void ShowInFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveList.SelectedItem is not InboxMessage { IsFile: true } message) return;

        try
        {
            if (File.Exists(message.FilePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{message.FilePath}\""));
            }
            else
            {
                var folder = Path.GetDirectoryName(message.FilePath) ?? PeerServer.DownloadDirectory;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                SetStatus("That file has moved; opened its folder instead.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open the folder: {ex.Message}");
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveList.SelectedItem is not InboxMessage message) return;

        try
        {
            // For a file the useful thing to copy is where it landed.
            Clipboard.SetText(message.IsFile ? message.FilePath : message.Text);
            SetStatus(message.IsFile ? "File path copied to clipboard." : "Copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetStatus($"Copy failed: {ex.Message}");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not InboxMessage message) return;

        message.IsSaved = true;
        _savedView.View.Refresh();
        if (PersistSaved())
        {
            UpdateInboxStatus();
            UpdateSavedStatus();
        }
        UpdateMessageButtons();
    }

    private void UnsaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedList.SelectedItem is not InboxMessage message) return;

        message.IsSaved = false;
        _savedView.View.Refresh();
        if (PersistSaved())
        {
            UpdateInboxStatus();
            UpdateSavedStatus();
        }
        UpdateMessageButtons();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveList.SelectedItem is not InboxMessage message) return;

        var wasSaved = message.IsSaved;
        var wasFile = message.IsFile;
        _inbox.Remove(message);
        MessageView.Text = "";
        SavedMessageView.Text = "";

        if (!wasSaved || PersistSaved())
        {
            UpdateInboxStatus();
            UpdateSavedStatus();
            // Removing the row must not look like it deleted the download.
            if (wasFile) SetStatus("Removed from the inbox. The file itself is still on disk.");
        }

        UpdateMessageButtons();
    }

    /// <summary>
    /// Rewrites the saved set to disk. Returns false when the write failed, in
    /// which case the reason is already on screen and the caller should not
    /// overwrite it with a routine status line.
    /// </summary>
    private bool PersistSaved()
    {
        try
        {
            _store.Save(_inbox.Where(m => m.IsSaved));
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not write {_store.Location}: {ex.Message}");
            return false;
        }
    }

    private void SetStatus(string text)
    {
        if (RootTabs.SelectedIndex == 1) SavedStatus.Text = text;
        else InboxStatus.Text = text;
    }

    private void UpdateMessageButtons()
    {
        var inboxMessage = InboxList.SelectedItem as InboxMessage;
        var inboxIsFile = inboxMessage is { IsFile: true };

        CopyButton.IsEnabled = inboxMessage is not null;
        DeleteButton.IsEnabled = inboxMessage is not null;
        SaveButton.IsEnabled = inboxMessage is { IsSaved: false };
        SaveButton.Content = inboxMessage is { IsSaved: true } ? "Saved" : "Save";
        OpenButton.IsEnabled = inboxIsFile;
        ShowInFolderButton.IsEnabled = inboxIsFile;

        var savedMessage = SavedList.SelectedItem as InboxMessage;
        var savedIsFile = savedMessage is { IsFile: true };

        SavedCopyButton.IsEnabled = savedMessage is not null;
        SavedDeleteButton.IsEnabled = savedMessage is not null;
        UnsaveButton.IsEnabled = savedMessage is not null;
        SavedOpenButton.IsEnabled = savedIsFile;
        SavedShowInFolderButton.IsEnabled = savedIsFile;
    }

    private void UpdateInboxStatus()
    {
        if (_inbox.Count == 0)
        {
            InboxStatus.Text = "Nothing received yet.";
            return;
        }

        var saved = _inbox.Count(m => m.IsSaved);
        var files = _inbox.Count(m => m.IsFile);
        var parts = new List<string> { $"{_inbox.Count} in this session" };
        if (files > 0) parts.Add($"{files} file{(files == 1 ? "" : "s")}");
        parts.Add($"{saved} saved to disk");
        InboxStatus.Text = string.Join("  ·  ", parts);
    }

    private void UpdateSavedStatus()
    {
        var saved = _inbox.Count(m => m.IsSaved);
        SavedStatus.Text = saved == 0 ? "Nothing saved yet." : $"{saved} saved message{(saved == 1 ? "" : "s")}.";
    }

    // ---------------------------------------------------------------- settings

    private void LoadSettingsIntoUi()
    {
        _loadingSettings = true;

        DisplayNameBox.Text = _settings.DisplayName;
        DownloadsFolderBox.Text = string.IsNullOrWhiteSpace(_settings.DownloadsFolder)
            ? PeerServer.DownloadDirectory
            : _settings.DownloadsFolder;

        if (_settings.MultiFileMode == MultiFileSendMode.Grouped) GroupedModeRadio.IsChecked = true;
        else ZipModeRadio.IsChecked = true;
        GroupTimeoutBox.Text = _settings.GroupTimeoutSeconds.ToString();

        NotifyToastCheck.IsChecked = _settings.NotifyToast;
        NotifySoundCheck.IsChecked = _settings.NotifySound;
        NotifyFlashCheck.IsChecked = _settings.NotifyTaskbarFlash;
        NotifyBadgeCheck.IsChecked = _settings.NotifyUnreadBadge;

        PinEnabledCheck.IsChecked = _settings.PinEnabled;
        if (_settings.PinMode == PinMode.RandomEachLaunch) PinRandomRadio.IsChecked = true;
        else PinPermanentRadio.IsChecked = true;
        PinBox.Text = _settings.Pin;
        UpdatePinOptionsVisibility();

        _loadingSettings = false;
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        try { _settingsStore.Save(_settings); }
        catch (Exception ex) { SetStatus($"Could not save settings: {ex.Message}"); }
    }

    private void DisplayNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _settings.DisplayName = DisplayNameBox.Text.Trim();
        SaveSettings();
    }

    private void BrowseDownloadsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a downloads folder",
            InitialDirectory = DownloadsFolderBox.Text
        };
        if (dialog.ShowDialog(this) != true) return;

        _settings.DownloadsFolder = dialog.FolderName;
        DownloadsFolderBox.Text = dialog.FolderName;
        SaveSettings();
    }

    private void ResetDownloadsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.DownloadsFolder = "";
        DownloadsFolderBox.Text = PeerServer.DownloadDirectory;
        SaveSettings();
    }

    private void MultiFileMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.MultiFileMode = GroupedModeRadio.IsChecked == true ? MultiFileSendMode.Grouped : MultiFileSendMode.Zip;
        SaveSettings();
    }

    private void GroupTimeoutBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (int.TryParse(GroupTimeoutBox.Text, out var seconds) && seconds > 0)
            _settings.GroupTimeoutSeconds = seconds;
        SaveSettings();
    }

    private void NotificationSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.NotifyToast = NotifyToastCheck.IsChecked == true;
        _settings.NotifySound = NotifySoundCheck.IsChecked == true;
        _settings.NotifyTaskbarFlash = NotifyFlashCheck.IsChecked == true;
        _settings.NotifyUnreadBadge = NotifyBadgeCheck.IsChecked == true;
        SaveSettings();
    }

    private void PinSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.PinEnabled = PinEnabledCheck.IsChecked == true;
        _settings.PinMode = PinRandomRadio.IsChecked == true ? PinMode.RandomEachLaunch : PinMode.Permanent;
        UpdatePinOptionsVisibility();
        SaveSettings();
    }

    private void PinBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.Pin = PinBox.Text.Trim();
        SaveSettings();
    }

    private void CopyPinButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PinBox.Text);
            SetStatus("PIN copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetStatus($"Copy failed: {ex.Message}");
        }
    }

    private void UpdatePinOptionsVisibility()
    {
        PinOptionsPanel.Visibility = PinEnabledCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _server?.Dispose();
        base.OnClosed(e);
    }
}
