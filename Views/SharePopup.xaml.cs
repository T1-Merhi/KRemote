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

    private bool _scanning;
    private bool _sending;

    /// <summary>Set when a send completes successfully, so the owner can show a toast.</summary>
    public string? SuccessMessage { get; private set; }

    public SharePopup()
    {
        InitializeComponent();

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

    private void PeerList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTargetLabel();

    private void UpdateTargetLabel()
    {
        TargetLabel.Text = PeerList.SelectedItem is Peer peer
            ? $"Sending to {peer.MachineName} ({peer.Address})."
            : "No device selected.";
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
                await PeerSender.SendTextAsync(peer.Address, title, text, CancellationToken.None);
                SuccessMessage = $"Sent to {peer.MachineName} at {DateTime.Now:HH:mm:ss}.";
            }
            else
            {
                await SendAttachmentsAsync(peer, title);
                SuccessMessage = $"Sent to {peer.MachineName} at {DateTime.Now:HH:mm:ss}.";
            }

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

    /// <summary>
    /// Interim sending path: each staged file goes as its own independent
    /// SendFileAsync call, in today's single-file wire format. Zip and
    /// grouped-message modes replace this once the protocol supports them.
    /// </summary>
    private async Task SendAttachmentsAsync(Peer peer, string? title)
    {
        TransferPanel.Visibility = Visibility.Visible;
        var files = _attachments.ToList();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            TransferProgress.Maximum = Math.Max(1, file.Size);
            TransferProgress.Value = 0;
            TransferStatus.Text = $"Sending {file.FileName} ({i + 1} of {files.Count})…";

            var progress = new Progress<long>(sent =>
            {
                TransferProgress.Value = sent;
                var percent = file.Size > 0 ? sent * 100.0 / file.Size : 100;
                TransferStatus.Text = $"{file.FileName}  ·  {percent:0}%  ({i + 1} of {files.Count})";
            });

            // Only the first file carries the shared title/text description.
            await PeerSender.SendFileAsync(peer.Address, i == 0 ? title : null, file.FilePath, progress, CancellationToken.None);
        }
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
