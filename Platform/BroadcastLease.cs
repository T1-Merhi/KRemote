namespace KRemote.Platform;

public static class BroadcastLease
{
    public static IDisposable Acquire()
    {
#if ANDROID
        try
        {
            var context = global::Android.App.Application.Context;
            var wifi = (global::Android.Net.Wifi.WifiManager?)
                context.GetSystemService(global::Android.Content.Context.WifiService);

            var lease = wifi?.CreateMulticastLock("KRemote.discovery");
            if (lease is null) return new NoLease();

            lease.SetReferenceCounted(true);
            lease.Acquire();
            return new AndroidLease(lease);
        }
        catch (Exception)
        {
            return new NoLease();
        }
#else
        return new NoLease();
#endif
    }

    private sealed class NoLease : IDisposable
    {
        public void Dispose()
        {
        }
    }

#if ANDROID
    private sealed class AndroidLease : IDisposable
    {
        private readonly global::Android.Net.Wifi.WifiManager.MulticastLock _lease;

        public AndroidLease(global::Android.Net.Wifi.WifiManager.MulticastLock lease)
        {
            _lease = lease;
        }

        public void Dispose()
        {
            try
            {
                if (_lease.IsHeld) _lease.Release();
                _lease.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
#endif
}
