using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using KRemote.Models;
using KRemote.Net;

namespace KRemote.Views;

/// <summary>
/// The compose flow, in its own popup: pick a device, write a title/text, attach
/// files, and send. Attaching files stages them as removable chips rather than
/// sending immediately; text alone still sends the moment Send is pressed.
/// </summary>
public partial class SharePopup : Window
{
    private const long MaxAttachmentsTotalBytes = 1L * 1024 * 1024 * 1024; // 1 GB
    private const long MaxTextBytes = 1L * 1024 * 1024;                    // 1 MB

    private readonly ObservableCollection<Peer> _peers = [];
    private readonly ObservableCollection<StagedAttachment> _attachments = [];
    private readonly AppSettings _settings;
    private readonly PinManager _pinManager;

    private bool _scanning;
    private bool _sending;
    private bool _unlocking;

    /// <summary>Set when a send completes successfully, so the owner can show a toast.</summary>
    public string? SuccessMessage { get; private set; }

    public SharePopup(AppSettings settings, PinManager pinManager)
    {
        InitializeComponent();

        _settings = settings;
        _pinManager = pinManager;
        PeerList.ItemsSource = _peers;
        AttachmentList.ItemsSource = _attachments;
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

    /// <summary>Shows the inline PIN row and disables composing when the selected peer is protected and not yet unlocked.</summary>
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

    // ---------------------------------------------------------------- compose

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

    // ---------------------------------------------------------------- send

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
            SuccessMessage = $"Sent to {peer.MachineName} at {DateTime.Now:HH:mm:ss}.";

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
        var files = _attachments.Select(a => a.FilePath).ToList();

        var progress = new Progress<(int fileIndex, int fileCount, long sent, long total)>(p =>
        {
            TransferProgress.Maximum = Math.Max(1, p.total);
            TransferProgress.Value = p.sent;
            var percent = p.total > 0 ? p.sent * 100.0 / p.total : 100;
            TransferStatus.Text = files.Count > 1
                ? $"Sending file {p.fileIndex + 1} of {p.fileCount}  ·  {percent:0}%"
                : $"{percent:0}%";
        });

        await PeerSender.SendFilesAsync(peer.Address, title, text, files, _settings.MultiFileMode, progress, CancellationToken.None, pin);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ---------------------------------------------------------------- error banner

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
