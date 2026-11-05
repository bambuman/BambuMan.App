namespace BambuMan.Implementations
{
    /// <summary>
    /// The runtime permissions a Bluetooth LE scale needs. Not MAUI's <see cref="Permissions.Bluetooth"/>: for an app
    /// targeting Android 12+ that one asks for nothing on Android 6–11, where a scan finds nothing without location.
    /// <list type="bullet">
    /// <item>Android 12+: BLUETOOTH_SCAN (declared <c>neverForLocation</c>) and BLUETOOTH_CONNECT — no location</item>
    /// <item>Android 6–11: ACCESS_FINE_LOCATION, which those versions require for any Bluetooth LE scan</item>
    /// </list>
    /// </summary>
    public class BluetoothScalePermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(31)
                ? [("android.permission.BLUETOOTH_SCAN", true), ("android.permission.BLUETOOTH_CONNECT", true)]
                : [("android.permission.ACCESS_FINE_LOCATION", true)];
    }
}
