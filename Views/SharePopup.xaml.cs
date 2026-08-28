using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using KRemote.Models;
using KRemote.Net;

namespace KRemote.Views;

public partial class SharePopup : Window
{
    private const long MaxAttachmentsTotalBytes = 1L * 1024 * 1024 * 1024;
    private const long MaxTextBytes = 1L * 1024 * 1024;

    private readonly ObservableCollection<Peer> _peers = [];
    private readonly ObservableCollection<StagedAttachment> _attachments = [];
    private readonly AppSettings _settings;
    private readonly PinManager _pinManager;

    private bool _scanning;
    private bool _sending;
    private bool _unlocking;
    private string? _transferSummary;

    public string? SuccessMessage { get; private set; }

    public SharePopup(AppSettings settings, PinManager pinManager)
    {
        InitializeComponent();

        _settings = settings;
        _pinManager = pinManager;
        PeerList.ItemsSource = _peers;
        AttachmentList.ItemsSource = _attachments;

        _attachments.CollectionChanged += (_, _) => UpdateAttachmentsPlaceholder();
        _peers.CollectionChanged += (_, _) => UpdatePeerPlaceholder();

        Loaded += (_, _) => Editor.Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            if (SendButton.IsEnabled) SendButton_Click(this, new RoutedEventArgs());
        }
    }

    private void UpdateAttachmentsPlaceholder() =>
        AttachmentsPlaceholder.Visibility = _attachments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdatePeerPlaceholder() =>
        PeerEmptyState.Visibility = _peers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning) return;

        _scanning = true;
        ScanButton.IsEnabled = false;
        ScanProgress.Value = 0;
        ScanProgress.Visibility = Visibility.Visible;
        ScanStatus.Text = "Scanning the local subnet…";
        _peers.Clear();
        _pinManager.ResetSession();

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
            UpdatePinGate();
        }
    }

    private void PeerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTargetLabel();
        UpdatePinGate();
    }

    private void UpdateTargetLabel()
    {
        TargetLabel.Text = PeerList.SelectedItem is Peer peer
            ? $"Sending to {peer.Label}."
            : "No device selected.";
    }

    private void UpdatePinGate()
    {
        var needsUnlock = PeerList.SelectedItem is Peer { IsProtected: true } peer && !_pinManager.IsUnlocked(peer.Address);

        PinUnlockRow.Visibility = needsUnlock ? Visibility.Visible : Visibility.Collapsed;
        PinEntryBox.Password = "";

        var composeEnabled = !needsUnlock;
        TitleRow.IsEnabled = composeEnabled;
        TextRow.IsEnabled = composeEnabled;
        AttachmentsRow.IsEnabled = composeEnabled;
        AttachFilesButton.IsEnabled = composeEnabled;
        SendButton.IsEnabled = composeEnabled;
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_unlocking) return;
        if (PeerList.SelectedItem is not Peer peer) return;

        var pin = PinEntryBox.Password.Trim();
        if (pin.Length != 4 || !pin.All(char.IsDigit))
        {
            ShowError("Enter the 4-digit PIN.");
            return;
        }

        _unlocking = true;
        HideError();

        try
        {
            await PeerSender.VerifyPinAsync(peer.Address, pin, CancellationToken.None);
            _pinManager.Unlock(peer.Address, pin);
            UpdatePinGate();
        }
        catch (Exception ex)
        {
            ShowError($"Could not unlock {peer.MachineName}: {ex.Message}");
        }
        finally
        {
            _unlocking = false;
        }
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        TitlePlaceholder.Visibility = string.IsNullOrEmpty(TitleBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AttachFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose files to attach",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        HideError();

        foreach (var path in dialog.FileNames)
        {
            if (_attachments.Any(a => a.FilePath == path)) continue;

            long size;
            try { size = new FileInfo(path).Length; }
            catch (Exception ex)
            {
                ShowError($"Could not read {Path.GetFileName(path)}: {ex.Message}");
                continue;
            }

            _attachments.Add(new StagedAttachment
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Size = size
            });
        }
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is StagedAttachment attachment)
            _attachments.Remove(attachment);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sending) return;
        HideError();

        if (PeerList.SelectedItem is not Peer peer)
        {
            ShowError("Select a device first.");
            return;
        }

        if (peer.IsProtected && !_pinManager.IsUnlocked(peer.Address))
        {
            ShowError("Enter this device's PIN first.");
            return;
        }

        var pin = peer.IsProtected ? _pinManager.GetCachedPin(peer.Address) : null;
        var text = Editor.Text;
        var title = TitleBox.Text;
        var hasText = !string.IsNullOrEmpty(text);
        var hasAttachments = _attachments.Count > 0;

        if (!hasText && !hasAttachments)
        {
            ShowError("Add some text or attach a file first.");
            return;
        }

        if (hasText && Encoding.UTF8.GetByteCount(text) > MaxTextBytes)
        {
            ShowError("Text is too large (limit 1 MB).");
            return;
        }

        if (hasAttachments && _attachments.Sum(a => a.Size) > MaxAttachmentsTotalBytes)
        {
            ShowError("Attached files exceed the 1 GB limit.");
            return;
        }

        _sending = true;
        SendButton.IsEnabled = false;
        AttachFilesButton.IsEnabled = false;

        try
        {
            if (!hasAttachments)
            {
                await PeerSender.SendTextAsync(peer.Address, title, text, CancellationToken.None, pin);
            }
            else
            {
                await SendAttachmentsAsync(peer, title, hasText ? text : null, pin);
            }

            SuccessMessage = _transferSummary is null
                ? $"Sent to {peer.MachineName} at {DateTime.Now:HH:mm:ss}."
                : $"Sent to {peer.MachineName} at {DateTime.Now:HH:mm:ss}.\n{_transferSummary}";

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Could not send to {peer.MachineName}: {ex.Message}");
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = true;
            AttachFilesButton.IsEnabled = true;
            TransferPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SendAttachmentsAsync(Peer peer, string? title, string? text, string? pin)
    {
        TransferPanel.Visibility = Visibility.Visible;
        TransferSpeed.Text = "";

        var files = _attachments.Select(a => a.FilePath).ToList();
        var sizes = _attachments.Select(a => a.Size).ToList();
        var grandTotal = sizes.Sum();

        var started = DateTime.UtcNow;
        var lastRender = DateTime.MinValue;
        var bytesOnWire = 0L;

        var progress = new Progress<(int fileIndex, int fileCount, long sent, long total)>(p =>
        {
            var completedBefore = _settings.MultiFileMode == MultiFileSendMode.Grouped && p.fileCount > 1
                ? sizes.Take(Math.Min(p.fileIndex, sizes.Count)).Sum()
                : 0;

            var overallTotal = p.fileCount > 1 && _settings.MultiFileMode == MultiFileSendMode.Grouped
                ? grandTotal
                : p.total;

            var overallSent = Math.Min(completedBefore + p.sent, overallTotal);
            bytesOnWire = overallTotal;

            TransferProgress.Maximum = Math.Max(1, overallTotal);
            TransferProgress.Value = overallSent;

            var now = DateTime.UtcNow;
            var complete = overallSent >= overallTotal;
            if (!complete && now - lastRender < TimeSpan.FromMilliseconds(100)) return;
            lastRender = now;

            var percent = overallTotal > 0 ? overallSent * 100.0 / overallTotal : 100;
            var counts = $"{InboxMessage.FormatSize(overallSent)} of {InboxMessage.FormatSize(overallTotal)}";

            TransferStatus.Text = p.fileCount > 1
                ? $"File {p.fileIndex + 1} of {p.fileCount}  ·  {percent:0}%  ·  {counts}"
                : $"{percent:0}%  ·  {counts}";

            var seconds = (now - started).TotalSeconds;
            if (seconds >= 0.25 && overallSent > 0)
            {
                var bytesPerSecond = (long)(overallSent / seconds);
                TransferSpeed.Text = $"{InboxMessage.FormatSize(bytesPerSecond)}/s";
            }
        });

        await PeerSender.SendFilesAsync(peer.Address, title, text, files, _settings.MultiFileMode, progress, CancellationToken.None, pin);

        var elapsed = Math.Max(0.001, (DateTime.UtcNow - started).TotalSeconds);
        var sentTotal = bytesOnWire > 0 ? bytesOnWire : grandTotal;
        var rate = InboxMessage.FormatSize((long)(sentTotal / elapsed));

        TransferProgress.Value = TransferProgress.Maximum;
        TransferStatus.Text = $"Done  ·  {InboxMessage.FormatSize(sentTotal)} in {elapsed:0.0}s";
        TransferSpeed.Text = $"{rate}/s";

        var label = files.Count == 1 ? "1 file" : $"{files.Count} files";
        _transferSummary = $"{label}  ·  {InboxMessage.FormatSize(sentTotal)} in {elapsed:0.0}s  ·  {rate}/s";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorBannerText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
    }
}
