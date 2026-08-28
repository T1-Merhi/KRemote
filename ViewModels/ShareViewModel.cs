using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KRemote.Models;
using KRemote.Net;
using KRemote.Services;

namespace KRemote.ViewModels;

public sealed partial class ShareViewModel : ObservableObject
{
    private const long MaxAttachmentsTotalBytes = 1L * 1024 * 1024 * 1024;
    private const long MaxTextBytes = 1L * 1024 * 1024;

    private readonly SettingsService _settings;
    private readonly PinManager _pins;
    private readonly PeerSender _sender;

    [ObservableProperty]
    private Peer? selectedPeer;

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private string pinEntry = "";

    [ObservableProperty]
    private string scanStatus = "Press Scan to find devices.";

    [ObservableProperty]
    private string error = "";

    [ObservableProperty]
    private double scanProgress;

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private bool isSending;

    [ObservableProperty]
    private string transferStatus = "";

    [ObservableProperty]
    private string transferSpeed = "";

    [ObservableProperty]
    private double transferProgress;

    public ShareViewModel(SettingsService settings, PinManager pins, PeerSender sender)
    {
        _settings = settings;
        _pins = pins;
        _sender = sender;
    }

    public ObservableCollection<Peer> Peers { get; } = [];

    public ObservableCollection<StagedAttachment> Attachments { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasPeers => Peers.Count > 0;

    public bool HasAttachments => Attachments.Count > 0;

    public bool NeedsUnlock => SelectedPeer is { IsProtected: true } peer && !_pins.IsUnlocked(peer.Address);

    public bool CanCompose => SelectedPeer is not null && !NeedsUnlock;

    public string TargetLabel => SelectedPeer is { } peer
        ? $"Sending to {peer.Label}."
        : "No device selected.";

    partial void OnSelectedPeerChanged(Peer? value)
    {
        PinEntry = "";
        Error = "";
        RaiseGateChanged();
    }

    private void RaiseGateChanged()
    {
        OnPropertyChanged(nameof(NeedsUnlock));
        OnPropertyChanged(nameof(CanCompose));
        OnPropertyChanged(nameof(TargetLabel));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanProgress = 0;
        ScanStatus = "Looking for devices…";
        Peers.Clear();
        _pins.ResetSession();
        OnPropertyChanged(nameof(HasPeers));

        var found = new Progress<Peer>(peer =>
        {
            if (Peers.Any(p => p.Address == peer.Address)) return;
            Peers.Add(peer);
            OnPropertyChanged(nameof(HasPeers));
            ScanStatus = Peers.Count == 1 ? "1 device found." : $"{Peers.Count} devices found.";
        });

        var progress = new Progress<(int done, int total)>(p =>
        {
            ScanProgress = p.total > 0 ? (double)p.done / p.total : 0;
        });

        try
        {
            await Discovery.RunAsync(found, progress, CancellationToken.None);

            if (Peers.Count == 0)
                ScanStatus = "No other KRemote device answered. Open it on the other device and allow it through the firewall.";
        }
        catch (Exception e)
        {
            ScanStatus = $"Scan failed: {e.Message}";
        }
        finally
        {
            IsScanning = false;
            RaiseGateChanged();
        }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (SelectedPeer is not { } peer) return;

        var pin = PinEntry.Trim();
        if (pin.Length != 4 || !pin.All(char.IsDigit))
        {
            Error = "Enter the 4-digit PIN.";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        try
        {
            await _sender.VerifyPinAsync(peer.Address, pin, CancellationToken.None);
            _pins.Unlock(peer.Address, pin);
            Error = "";
        }
        catch (Exception e)
        {
            Error = $"Could not unlock {peer.MachineName}: {e.Message}";
        }

        RaiseGateChanged();
    }

    [RelayCommand]
    private async Task AttachAsync()
    {
        try
        {
            var picked = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Choose files to attach"
            });

            if (picked is null) return;

            foreach (var file in picked)
            {
                var path = await StageAsync(file);
                if (path is null) continue;
                if (Attachments.Any(a => a.FilePath == path)) continue;

                Attachments.Add(new StagedAttachment
                {
                    FilePath = path,
                    FileName = Path.GetFileName(path),
                    Size = new FileInfo(path).Length
                });
            }

            OnPropertyChanged(nameof(HasAttachments));
        }
        catch (Exception e)
        {
            Error = $"Could not attach: {e.Message}";
            OnPropertyChanged(nameof(HasError));
        }
    }

    private static async Task<string?> StageAsync(FileResult file)
    {
        if (!OperatingSystem.IsAndroid() && File.Exists(file.FullPath)) return file.FullPath;

        var target = Path.Combine(FileSystem.CacheDirectory, "outgoing");
        Directory.CreateDirectory(target);

        var path = Path.Combine(target, file.FileName);

        await using var source = await file.OpenReadAsync();
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination);

        return path;
    }

    [RelayCommand]
    private void RemoveAttachment(StagedAttachment attachment)
    {
        Attachments.Remove(attachment);
        OnPropertyChanged(nameof(HasAttachments));
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsSending) return;

        Error = "";

        if (SelectedPeer is not { } peer)
        {
            Error = "Select a device first.";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        if (peer.IsProtected && !_pins.IsUnlocked(peer.Address))
        {
            Error = "Enter this device's PIN first.";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        var pin = peer.IsProtected ? _pins.GetCachedPin(peer.Address) : null;
        var hasText = !string.IsNullOrEmpty(Text);

        if (!hasText && Attachments.Count == 0)
        {
            Error = "Add some text or attach a file first.";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        if (hasText && Encoding.UTF8.GetByteCount(Text) > MaxTextBytes)
        {
            Error = "Text is too large (limit 1 MB).";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        if (Attachments.Sum(a => a.Size) > MaxAttachmentsTotalBytes)
        {
            Error = "Attached files exceed the 1 GB limit.";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        IsSending = true;

        try
        {
            if (Attachments.Count == 0)
            {
                await _sender.SendTextAsync(
                    peer.Address, _settings.Current.DisplayName, Title, Text, CancellationToken.None, pin);
            }
            else
            {
                await SendAttachmentsAsync(peer, pin);
            }

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception e)
        {
            Error = $"Could not send to {peer.MachineName}: {e.Message}";
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsSending = false;
        }
    }

    private async Task SendAttachmentsAsync(Peer peer, string? pin)
    {
        var files = Attachments.Select(a => a.FilePath).ToList();
        var sizes = Attachments.Select(a => a.Size).ToList();
        var grandTotal = sizes.Sum();
        var grouped = _settings.Current.MultiFileMode == MultiFileSendMode.Grouped;

        var started = DateTime.UtcNow;
        var lastRender = DateTime.MinValue;

        var progress = new Progress<(int fileIndex, int fileCount, long sent, long total)>(p =>
        {
            var completedBefore = grouped && p.fileCount > 1
                ? sizes.Take(Math.Min(p.fileIndex, sizes.Count)).Sum()
                : 0;

            var overallTotal = grouped && p.fileCount > 1 ? grandTotal : p.total;
            var overallSent = Math.Min(completedBefore + p.sent, overallTotal);

            TransferProgress = overallTotal > 0 ? (double)overallSent / overallTotal : 0;

            var now = DateTime.UtcNow;
            var complete = overallSent >= overallTotal;
            if (!complete && now - lastRender < TimeSpan.FromMilliseconds(100)) return;
            lastRender = now;

            var percent = overallTotal > 0 ? overallSent * 100.0 / overallTotal : 100;
            var counts = $"{SessionMessage.FormatSize(overallSent)} of {SessionMessage.FormatSize(overallTotal)}";

            TransferStatus = p.fileCount > 1
                ? $"File {p.fileIndex + 1} of {p.fileCount}  ·  {percent:0}%  ·  {counts}"
                : $"{percent:0}%  ·  {counts}";

            var seconds = (now - started).TotalSeconds;
            if (seconds >= 0.25 && overallSent > 0)
                TransferSpeed = $"{SessionMessage.FormatSize((long)(overallSent / seconds))}/s";
        });

        await _sender.SendFilesAsync(
            peer.Address, _settings.Current.DisplayName, Title, string.IsNullOrEmpty(Text) ? null : Text,
            files, _settings.Current.MultiFileMode, progress, CancellationToken.None, pin);
    }

    [RelayCommand]
    private async Task CancelAsync() => await Shell.Current.GoToAsync("..");
}
