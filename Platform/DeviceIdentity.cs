namespace KRemote.Platform;

public sealed class DeviceIdentity : IDeviceIdentity
{
    private readonly Lazy<string> _machineName = new(Resolve);

    public string MachineName => _machineName.Value;

    private static string Resolve()
    {
#if ANDROID
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver;
            if (resolver is not null)
            {
                var name = Android.Provider.Settings.Global.GetString(resolver, "device_name");
                if (!string.IsNullOrWhiteSpace(name)) return name!;
            }
        }
        catch (Exception)
        {
        }

        var model = $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}".Trim();
        return string.IsNullOrWhiteSpace(model) ? "Android device" : model;
#else
        return Environment.MachineName;
#endif
    }
}
