using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KRemote.Models;
using KRemote.Net;
using KRemote.Storage;

namespace KRemote;

/// <summary>
/// Every instance is both ends of the link: it listens for text from other PCs
/// and it can send text to one of them. The three panes map to that directly --
/// scan results on the left, the editor top right, the inbox bottom right.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<Peer> _peers = [];
    private readonly ObservableCollection<TextMessage> _inbox = [];
    private readonly MessageStore _store = new();

    private PeerServer? _server;
    private bool _scanning;

    public MainWindow()
    {
        InitializeComponent();

        PeerList.ItemsSource = _peers;
        InboxList.ItemsSource = _inbox;

        // Saved messages are the only ones that survive a restart; they come
        // back newest first, the same order live arrivals are inserted in.
        foreach (var message in _store.Load().OrderByDescending(m => m.ReceivedAt))
            _inbox.Add(message);

        UpdateInboxStatus();
        UpdateMessageButtons();
        StartServer();
    }

    // ---------------------------------------------------------------- receiving

    private void StartServer()
    {
        _server = new PeerServer();
        _server.MessageReceived += OnMessageReceived;

        try
        {
            _server.Start();
            SelfStatus.Text = $"This PC: {Environment.MachineName}  ·  listening on port {Protocol.Port}";
        }
        catch (SocketException)
        {
            // Almost always a second copy of KRemote on this machine. Sending
            // still works, so keep running instead of failing to start.
            SelfStatus.Text = $"Port {Protocol.Port} is already in use, so this window cannot receive text. " +
                              "Close the other KRemote instance on this PC and restart. Sending still works.";
            SelfStatus.Foreground = (Brush)FindResource("Danger");
        }
    }

    private void OnMessageReceived(TextMessage message)
    {
        // Raised on a socket thread; the inbox is a UI-bound collection.
        // Arrival is deliberately silent: no popup, no focus steal.
        Dispatcher.Invoke(() =>
        {
            _inbox.Insert(0, message);
            UpdateInboxStatus();
        });
    }

    // ---------------------------------------------------------------- scanning

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning) return;

        _scanning = true;
        ScanButton.IsEnabled = false;
        ScanProgress.Value = 0;
        ScanProgress.Visibility = Visibility.Visible;
        ScanStatus.Text = "Scanning the local subnet…";
        _peers.Clear();

        var progress = new Progress<(int done, int total)>(p =>
        {
            ScanProgress.Maximum = Math.Max(1, p.total);
            ScanProgress.Value = p.done;
        });

        try
        {
            var found = await PeerScanner.ScanAsync(progress, CancellationToken.None);
            foreach (var peer in found) _peers.Add(peer);

            ScanStatus.Text = found.Count switch
            {
                0 => "No other KRemote app answered. Open it on the other PC and allow it through Windows Firewall.",
                1 => "1 device found.",
                _ => $"{found.Count} devices found."
            };
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            _scanning = false;
            ScanButton.IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
            UpdateTargetLabel();
        }
    }

    private void PeerList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => UpdateTargetLabel();

    private void UpdateTargetLabel()
    {
        TargetLabel.Text = PeerList.SelectedItem is Peer peer
            ? $"Sending to {peer.MachineName} ({peer.Address})."
            : "No device selected.";
    }

    // ---------------------------------------------------------------- sending

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (PeerList.SelectedItem is not Peer peer)
        {
            SendStatus.Text = "Select a device on the left first.";
            return;
        }

        var text = Editor.Text;
        if (string.IsNullOrEmpty(text))
        {
            SendStatus.Text = "Type some text before submitting.";
            return;
        }

        SubmitButton.IsEnabled = false;
        SendStatus.Text = $"Sending to {peer.MachineName}…";

        try
        {
            await PeerSender.SendAsync(peer.Address, text, CancellationToken.None);
            Editor.Clear();
            SendStatus.Text = $"Sent to {peer.MachineName} at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            SendStatus.Text = $"Could not send to {peer.MachineName}: {ex.Message}";
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter belongs to the editor, so submitting takes the modifier.
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            SubmitButton_Click(this, new RoutedEventArgs());
        }
    }

    // ---------------------------------------------------------------- inbox

    private void InboxList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        MessageView.Text = InboxList.SelectedItem is TextMessage message ? message.Text : "";
        UpdateMessageButtons();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not TextMessage message) return;

        try
        {
            Clipboard.SetText(message.Text);
            InboxStatus.Text = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            InboxStatus.Text = $"Copy failed: {ex.Message}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not TextMessage message) return;

        message.IsSaved = true;
        if (PersistSaved()) UpdateInboxStatus();
        UpdateMessageButtons();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not TextMessage message) return;

        var wasSaved = message.IsSaved;
        _inbox.Remove(message);
        MessageView.Text = "";

        if (!wasSaved || PersistSaved()) UpdateInboxStatus();
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
            InboxStatus.Text = $"Could not write {_store.Location}: {ex.Message}";
            return false;
        }
    }

    private void UpdateMessageButtons()
    {
        var message = InboxList.SelectedItem as TextMessage;
        CopyButton.IsEnabled = message is not null;
        DeleteButton.IsEnabled = message is not null;
        SaveButton.IsEnabled = message is { IsSaved: false };
        SaveButton.Content = message is { IsSaved: true } ? "Saved" : "Save";
    }

    private void UpdateInboxStatus()
    {
        if (_inbox.Count == 0)
        {
            InboxStatus.Text = "Nothing received yet.";
            return;
        }

        var saved = _inbox.Count(m => m.IsSaved);
        InboxStatus.Text = $"{_inbox.Count} in this session  ·  {saved} saved to disk";
    }

    protected override void OnClosed(EventArgs e)
    {
        _server?.Dispose();
        base.OnClosed(e);
    }
}
