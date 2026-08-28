using System.Collections.ObjectModel;
using System.Net.Sockets;
using KRemote.Models;
using KRemote.Net;
using KRemote.Platform;
using KRemote.Storage;

namespace KRemote.Services;

public sealed class InboxService : IDisposable
{
    private readonly MessageStore _store;
    private readonly SettingsService _settings;
    private readonly INotifier _notifier;
    private readonly IDeviceIdentity _identity;
    private readonly PinManager _pins;

    private PeerServer? _server;
    private PeerBeacon? _beacon;
    private bool _loaded;

    public InboxService(
        MessageStore store,
        SettingsService settings,
        INotifier notifier,
        IDeviceIdentity identity,
        PinManager pins)
    {
        _store = store;
        _settings = settings;
        _notifier = notifier;
        _identity = identity;
        _pins = pins;
    }

    public ObservableCollection<InboxMessage> Messages { get; } = [];

    public string StoreLocation => _store.Location;

    public bool IsListening { get; private set; }

    public string? ListenError { get; private set; }

    public event Action? Changed;

    public event Action<string, long, long>? TransferProgress;

    public void LoadSaved()
    {
        if (_loaded) return;
        _loaded = true;

        foreach (var message in _store.Load().OrderByDescending(m => m.ReceivedAt))
        {
            message.IsUnread = false;
            Messages.Add(message);
        }

        Changed?.Invoke();
    }

    public void StartListening()
    {
        if (_server is not null) return;

        _server = new PeerServer(
            _identity,
            _settings.Paths,
            () => _settings.Current,
            () => _pins.CurrentPin);

        _server.MessageReceived += OnMessageReceived;
        _server.TransferProgress += OnTransferProgress;

        try
        {
            _server.Start();
            IsListening = true;
            ListenError = null;
        }
        catch (SocketException e)
        {
            IsListening = false;
            ListenError = $"Port {Protocol.Port} is already in use: {e.Message}";
        }

        _beacon = new PeerBeacon(_identity, () => _settings.Current);

        try { _beacon.Start(); }
        catch (SocketException) { }

        Changed?.Invoke();
    }

    public void StopListening()
    {
        if (_server is not null)
        {
            _server.MessageReceived -= OnMessageReceived;
            _server.TransferProgress -= OnTransferProgress;
            _server.Dispose();
            _server = null;
        }

        _beacon?.Dispose();
        _beacon = null;

        IsListening = false;
        Changed?.Invoke();
    }

    private void OnMessageReceived(InboxMessage message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Insert(0, message);
            _notifier.NotifyMessageReceived(message);
            Changed?.Invoke();
        });
    }

    private void OnTransferProgress(string fileName, long received, long total)
    {
        MainThread.BeginInvokeOnMainThread(() => TransferProgress?.Invoke(fileName, received, total));
    }

    public void Remove(InboxMessage message)
    {
        var wasSaved = message.IsSaved;
        Messages.Remove(message);
        if (wasSaved) PersistSaved();
        Changed?.Invoke();
    }

    public void SetSaved(InboxMessage message, bool saved)
    {
        message.IsSaved = saved;
        PersistSaved();
        Changed?.Invoke();
    }

    public string? PersistSaved()
    {
        try
        {
            _store.Save(Messages.Where(m => m.IsSaved));
            return null;
        }
        catch (Exception e)
        {
            return $"Could not write {_store.Location}: {e.Message}";
        }
    }

    public int UnreadCount => Messages.Count(m => m.IsUnread);

    public void NotifyChanged() => Changed?.Invoke();

    public void Dispose() => StopListening();
}
