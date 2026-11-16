namespace BambuMan
{
    /// <summary>
    /// Android 17 (API 37) local network protection: an app targeting it can't reach LAN addresses (private IP ranges,
    /// <c>.local</c> hosts) without <c>ACCESS_LOCAL_NETWORK</c>. Spoolman / Bambuddy are usually self-hosted on the
    /// user's LAN, so it is requested before the app first talks to the server. Public domains and VPN addresses
    /// don't need it, so a denial doesn't stop the connection attempt — it only explains why a LAN server fails.
    /// </summary>
    public static class LocalNetworkPermission
    {
        public const string DeniedMessage = "Local network access was denied, so a Spoolman / Bambuddy server on your local network can't be reached. Allow \"Nearby devices\" for BambuMan in Android settings.";

        /// <summary>Asks for local network access if the OS needs it. True when access is granted or not needed.</summary>
        public static async Task<bool> EnsureAsync()
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(37)) return true;

            return await MainThread.InvokeOnMainThreadAsync(RequestAsync);
#else
            return await Task.FromResult(true);
#endif
        }

#if ANDROID
        [System.Runtime.Versioning.SupportedOSPlatform("android37.0")]
        private static async Task<bool> RequestAsync()
        {
            var status = await Permissions.CheckStatusAsync<AndroidLocalNetwork>();
            if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<AndroidLocalNetwork>();

            return status == PermissionStatus.Granted;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("android37.0")]
        private class AndroidLocalNetwork : Permissions.BasePlatformPermission
        {
            public override (string androidPermission, bool isRuntime)[] RequiredPermissions => [(Android.Manifest.Permission.AccessLocalNetwork, true)];
        }
#endif
    }
}
