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
using KRemote.Notifications;
using KRemote.Storage;

namespace KRemote;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<InboxMessage> _inbox = [];
    private readonly MessageStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private PinManager _pinManager = null!;
    private NotificationService _notifications = null!;

    private readonly CollectionViewSource _savedView = new();

    private PeerServer? _server;
    private bool _loadingSettings;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsStore.Load();

        if (!_settings.FirstRunPromptShown)
        {
            var prompt = new Views.FirstRunPinPrompt();
            if (prompt.ShowDialog() == true)
            {
                prompt.Apply(_settings);
                _settingsStore.Save(_settings);
            }
        }

        _pinManager = new PinManager(_settings);
        _notifications = new NotificationService(() => _settings, this);

        InboxList.ItemsSource = _inbox;

        _savedView.Source = _inbox;
        _savedView.Filter += (_, e) => e.Accepted = e.Item is InboxMessage { IsSaved: true };
        SavedList.ItemsSource = _savedView.View;

        foreach (var message in _store.Load().OrderByDescending(m => m.ReceivedAt))
        {
            message.IsUnread = false;
            _inbox.Add(message);
        }

        LoadSettingsIntoUi();

        UpdateInboxStatus();
        UpdateSavedStatus();
        UpdateMessageButtons();
        UpdateUnreadBadge();
        UpdateEmptyStates();
        StartServer();
    }

    private void StartServer()
    {
        _server = new PeerServer(() => _settings, () => _pinManager.CurrentPin);
        _server.MessageReceived += OnMessageReceived;
        _server.TransferProgress += OnTransferProgress;

        try
        {
            _server.Start();
        }
        catch (SocketException)
        {
        }
    }

    private void OnMessageReceived(InboxMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            _inbox.Insert(0, message);
            UpdateInboxStatus();
            UpdateSavedStatus();
            UpdateUnreadBadge();
            UpdateEmptyStates();
            _notifications.NotifyMessageReceived(message);
        });
    }

    private void OnTransferProgress(string fileName, long received, long total)
    {
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

    private void RootTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source != RootTabs) return;
        ShareButton.Visibility = RootTabs.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.N &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            if (ShareButton.Visibility == Visibility.Visible) ShareButton_Click(this, new RoutedEventArgs());
            return;
        }

        if (e.Key == System.Windows.Input.Key.Delete &&
            (InboxList.IsKeyboardFocusWithin || SavedList.IsKeyboardFocusWithin) &&
            ActiveList.SelectedItem is InboxMessage)
        {
            e.Handled = true;
            DeleteButton_Click(this, new RoutedEventArgs());
        }
    }

    private void ShareButton_Click(object sender, RoutedEventArgs e)
    {
        var popup = new Views.SharePopup(_settings, _pinManager) { Owner = this };
        if (popup.ShowDialog() == true && popup.SuccessMessage is not null)
            ShowToast(popup.SuccessMessage, success: true);
    }

    public void ShowToast(string message, bool success)
    {
        var dialog = new Views.ToastWindow(message, success) { Owner = this };
        dialog.ShowDialog();
    }

    private void InboxList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        MessageView.Text = InboxList.SelectedItem is InboxMessage message ? Describe(message) : "";
        MarkRead(InboxList.SelectedItem as InboxMessage);
        UpdateMessageButtons();
        UpdateEmptyStates();
    }

    private void SavedList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SavedMessageView.Text = SavedList.SelectedItem is InboxMessage message ? Describe(message) : "";
        MarkRead(SavedList.SelectedItem as InboxMessage);
        UpdateMessageButtons();
        UpdateEmptyStates();
    }

    private void UpdateEmptyStates()
    {
        InboxEmptyState.Visibility = _inbox.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SavedEmptyState.Visibility = _inbox.Any(m => m.IsSaved) ? Visibility.Collapsed : Visibility.Visible;

        MessagePlaceholder.Visibility = InboxList.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
        SavedMessagePlaceholder.Visibility = SavedList.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MarkRead(InboxMessage? message)
    {
        if (message is not { IsUnread: true }) return;
        message.IsUnread = false;
        UpdateUnreadBadge();
    }

    private void UpdateUnreadBadge()
    {
        var count = _settings.NotifyUnreadBadge ? _inbox.Count(m => m.IsUnread) : 0;
        UnreadBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadBadgeText.Text = count > 99 ? "99+" : count.ToString();
    }

    private System.Windows.Controls.ListBox ActiveList => RootTabs.SelectedIndex == 1 ? SavedList : InboxList;

    private static string Describe(InboxMessage message)
    {
        if (!message.IsFile) return message.Text;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Title)) lines.Add(message.Title.Trim());
        if (!string.IsNullOrWhiteSpace(message.Text)) { lines.Add(message.Text.Trim()); lines.Add(""); }

        if (message.IsGroup)
        {
            lines.Add($"{message.Attachments.Count} files:");
            foreach (var attachment in message.Attachments)
                lines.Add($"  {attachment.FileName}  ({InboxMessage.FormatSize(attachment.FileSize)})");
        }
        else
        {
            lines.Add(message.FileName);
            lines.Add($"{InboxMessage.FormatSize(message.FileSize)}  ({message.FileSize:N0} bytes)");
        }

        lines.Add($"From {message.From} ({message.FromAddress}) at {message.ReceivedAt:MMM d, yyyy HH:mm:ss}");

        if (!message.IsGroup)
        {
            lines.Add("");
            lines.Add("Saved to:");
            lines.Add(message.FilePath);
        }

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
        UpdateEmptyStates();
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
        UpdateEmptyStates();
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
            if (wasFile) SetStatus("Removed from the inbox. The file itself is still on disk.");
        }

        UpdateMessageButtons();
        UpdateEmptyStates();
    }

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
        if (_loadingSettings) return;

        var valid = int.TryParse(GroupTimeoutBox.Text, out var seconds) && seconds > 0;
        GroupTimeoutHint.Foreground = (Brush)FindResource(valid ? "Muted" : "Danger");

        if (!valid) return;

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
        UpdateUnreadBadge();
        SaveSettings();
    }

    private void PinSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.PinEnabled = PinEnabledCheck.IsChecked == true;
        UpdatePinOptionsVisibility();
        ValidatePin();
        SaveSettings();
    }

    private void PinBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.Pin = PinBox.Text.Trim();
        ValidatePin();
        SaveSettings();
    }

    private void ValidatePin()
    {
        var pin = PinBox.Text.Trim();
        var valid = pin.Length == 4 && pin.All(char.IsDigit);

        if (PinEnabledCheck.IsChecked != true || valid)
        {
            PinHint.Text = "Four digits.";
            PinHint.Foreground = (Brush)FindResource("Muted");
            return;
        }

        PinHint.Text = "Enter 4 digits, or nobody will be able to send to this PC.";
        PinHint.Foreground = (Brush)FindResource("Danger");
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
