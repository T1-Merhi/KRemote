using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KRemote.Models;
using KRemote.Net;
using KRemote.Storage;

namespace KRemote;

/// <summary>
/// Every instance is both ends of the link: it listens for text and files from
/// other PCs and can send either to one of them. The three panes map to that
/// directly -- scan results on the left, the composer top right, the inbox
/// bottom right.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<Peer> _peers = [];
    private readonly ObservableCollection<InboxMessage> _inbox = [];
    private readonly MessageStore _store = new();

    private PeerServer? _server;
    private bool _scanning;
    private bool _sending;

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
        _server.TransferProgress += OnTransferProgress;

        try
        {
            _server.Start();
            SelfStatus.Text = $"This PC: {Environment.MachineName}  ·  listening on port {Protocol.Port}";
        }
        catch (SocketException)
        {
            // Almost always a second copy of KRemote on this machine. Sending
            // still works, so keep running instead of failing to start.
            SelfStatus.Text = $"Port {Protocol.Port} is already in use, so this window cannot receive. " +
                              "Close the other KRemote instance on this PC and restart. Sending still works.";
            SelfStatus.Foreground = (Brush)FindResource("Danger");
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

    private void TitleBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // WPF has no native watermark, so the hint is a label we hide on input.
        TitlePlaceholder.Visibility = string.IsNullOrEmpty(TitleBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        if (!TryGetTarget(out var peer)) return;

        var text = Editor.Text;
        if (string.IsNullOrEmpty(text))
        {
            SendStatus.Text = "Type some text before submitting.";
            return;
        }

        BeginSend($"Sending to {peer!.MachineName}…");
        try
        {
            await PeerSender.SendTextAsync(peer.Address, TitleBox.Text, text, CancellationToken.None);
            Editor.Clear();
            TitleBox.Clear();
            SendStatus.Text = $"Sent to {peer.MachineName} at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            SendStatus.Text = $"Could not send to {peer.MachineName}: {ex.Message}";
        }
        finally
        {
            EndSend();
        }
    }

    private async void SendFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        if (!TryGetTarget(out var peer)) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a file to send",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        var path = dialog.FileName;
        var fileName = Path.GetFileName(path);

        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            SendStatus.Text = $"Could not read {fileName}: {ex.Message}";
            return;
        }

        BeginSend($"Sending {fileName} to {peer!.MachineName}…");
        TransferPanel.Visibility = Visibility.Visible;
        TransferProgress.Maximum = Math.Max(1, size);
        TransferProgress.Value = 0;
        TransferStatus.Text = $"{fileName}  ·  0%  of {InboxMessage.FormatSize(size)}";

        var started = DateTime.Now;
        var progress = new Progress<long>(sent =>
        {
            TransferProgress.Value = sent;
            var percent = size > 0 ? sent * 100.0 / size : 100;
            TransferStatus.Text = $"{fileName}  ·  {percent:0}%  " +
                                  $"({InboxMessage.FormatSize(sent)} of {InboxMessage.FormatSize(size)})";
        });

        try
        {
            await PeerSender.SendFileAsync(peer.Address, TitleBox.Text, path, progress, CancellationToken.None);

            var seconds = Math.Max(0.001, (DateTime.Now - started).TotalSeconds);
            TitleBox.Clear();
            TransferStatus.Text = $"{fileName}  ·  done in {seconds:0.0}s " +
                                  $"({InboxMessage.FormatSize((long)(size / seconds))}/s)";
            SendStatus.Text = $"Sent {fileName} to {peer.MachineName} at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            TransferStatus.Text = $"{fileName}  ·  failed";
            SendStatus.Text = $"Could not send {fileName} to {peer.MachineName}: {ex.Message}";
        }
        finally
        {
            EndSend();
        }
    }

    private bool TryGetTarget(out Peer? peer)
    {
        peer = PeerList.SelectedItem as Peer;
        if (peer is not null) return true;

        SendStatus.Text = "Select a device on the left first.";
        return false;
    }

    private void BeginSend(string status)
    {
        _sending = true;
        SubmitButton.IsEnabled = false;
        SendFileButton.IsEnabled = false;
        SendStatus.Text = status;
    }

    private void EndSend()
    {
        _sending = false;
        SubmitButton.IsEnabled = true;
        SendFileButton.IsEnabled = true;
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
        MessageView.Text = InboxList.SelectedItem is InboxMessage message ? Describe(message) : "";
        UpdateMessageButtons();
    }

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
        if (InboxList.SelectedItem is not InboxMessage { IsFile: true } message) return;

        if (!File.Exists(message.FilePath))
        {
            InboxStatus.Text = "That file is no longer at the saved path.";
            return;
        }

        try
        {
            // ShellExecute so the file opens in whatever it is associated with.
            Process.Start(new ProcessStartInfo(message.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            InboxStatus.Text = $"Could not open the file: {ex.Message}";
        }
    }

    private void ShowInFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not InboxMessage { IsFile: true } message) return;

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
                InboxStatus.Text = "That file has moved; opened its folder instead.";
            }
        }
        catch (Exception ex)
        {
            InboxStatus.Text = $"Could not open the folder: {ex.Message}";
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not InboxMessage message) return;

        try
        {
            // For a file the useful thing to copy is where it landed.
            Clipboard.SetText(message.IsFile ? message.FilePath : message.Text);
            InboxStatus.Text = message.IsFile ? "File path copied to clipboard." : "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            InboxStatus.Text = $"Copy failed: {ex.Message}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not InboxMessage message) return;

        message.IsSaved = true;
        if (PersistSaved()) UpdateInboxStatus();
        UpdateMessageButtons();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not InboxMessage message) return;

        var wasSaved = message.IsSaved;
        var wasFile = message.IsFile;
        _inbox.Remove(message);
        MessageView.Text = "";

        if (!wasSaved || PersistSaved())
        {
            UpdateInboxStatus();
            // Removing the row must not look like it deleted the download.
            if (wasFile) InboxStatus.Text = "Removed from the inbox. The file itself is still on disk.";
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
            InboxStatus.Text = $"Could not write {_store.Location}: {ex.Message}";
            return false;
        }
    }

    private void UpdateMessageButtons()
    {
        var message = InboxList.SelectedItem as InboxMessage;
        var isFile = message is { IsFile: true };

        CopyButton.IsEnabled = message is not null;
        DeleteButton.IsEnabled = message is not null;
        SaveButton.IsEnabled = message is { IsSaved: false };
        SaveButton.Content = message is { IsSaved: true } ? "Saved" : "Save";
        OpenButton.IsEnabled = isFile;
        ShowInFolderButton.IsEnabled = isFile;
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

    protected override void OnClosed(EventArgs e)
    {
        _server?.Dispose();
        base.OnClosed(e);
    }
}
