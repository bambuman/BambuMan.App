namespace BambuMan.Shared.Enums
{
    /// <summary>Where the connection to the chosen Bluetooth scale stands.</summary>
    public enum ScaleConnectionState
    {
        /// <summary>The scale is turned off in settings, or no scale has been chosen yet.</summary>
        Off,

        /// <summary>Bluetooth is turned off, or the phone has no Bluetooth LE.</summary>
        BluetoothOff,

        /// <summary>The Bluetooth permissions were not granted.</summary>
        NoPermission,

        Connecting,

        Connected,

        /// <summary>The scale is out of reach or turned off; the connection is retried until it is back.</summary>
        Reconnecting,

        /// <summary>The device was reached but offers neither scale service.</summary>
        NotAScale
    }

    public static class ScaleConnectionStateExtensions
    {
        /// <summary>How the state is labelled in the ui.</summary>
        public static string DisplayName(this ScaleConnectionState state) => state switch
        {
            ScaleConnectionState.BluetoothOff => "Bluetooth is off",
            ScaleConnectionState.NoPermission => "No Bluetooth permission",
            ScaleConnectionState.Connecting => "Connecting",
            ScaleConnectionState.Connected => "Connected",
            ScaleConnectionState.Reconnecting => "Waiting for the scale",
            ScaleConnectionState.NotAScale => "Not a supported scale",
            _ => "Off"
        };
    }
}
