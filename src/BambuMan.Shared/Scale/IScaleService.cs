using BambuMan.Shared.Enums;
using System.ComponentModel;

namespace BambuMan.Shared.Scale
{
    /// <summary>A scale found by a Bluetooth scan.</summary>
    /// <param name="Id">The id the scale is connected and remembered by.</param>
    /// <param name="Name">The advertised name; many scales advertise none.</param>
    /// <param name="Rssi">Signal strength (dBm) — the nearest scale has the highest.</param>
    public record ScaleDevice(Guid Id, string? Name, int Rssi);

    /// <summary>
    /// A Bluetooth scale: finds scales, keeps the chosen one connected (reconnecting when it comes back in reach) and
    /// passes on the weights it sends. Raises <see cref="INotifyPropertyChanged"/> for its state, from whichever thread
    /// the change came on.
    /// </summary>
    public interface IScaleService : INotifyPropertyChanged
    {
        ScaleConnectionState State { get; }

        /// <summary>The scale being connected to, null while none is.</summary>
        Guid? DeviceId { get; }

        string? DeviceName { get; }

        /// <summary>The protocol of the connected scale, known once it is connected.</summary>
        ScaleProtocol? Protocol { get; }

        /// <summary>The latest weight sent, null until the first one (and again after the scale is stopped).</summary>
        ScaleReading? LastReading { get; }

        /// <summary>The connected scale can be zeroed from the app (BambuMan scales with a control characteristic).</summary>
        bool CanTare { get; }

        bool IsScanning { get; }

        /// <summary>Raised for every valid weight the connected scale sends, on a background thread.</summary>
        event Action<ScaleReading>? ReadingReceived;

        /// <summary>Asks for the Bluetooth permissions the scale needs; true when they are granted.</summary>
        Task<bool> RequestPermissionsAsync();

        /// <summary>Scans for scales advertising a supported service, calling <paramref name="found"/> for each new one, until the scan times out or is cancelled.</summary>
        Task ScanAsync(Action<ScaleDevice> found, CancellationToken cancellationToken);

        /// <summary>Connects to <paramref name="deviceId"/> and keeps it connected until <see cref="StopAsync"/>.</summary>
        Task StartAsync(Guid deviceId, string? deviceName);

        /// <summary>Disconnects and stops reconnecting.</summary>
        Task StopAsync();

        /// <summary>Zeroes the scale; false when it can't be (see <see cref="CanTare"/>) or the write failed.</summary>
        Task<bool> TareAsync();
    }
}
