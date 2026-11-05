using BambuMan.Shared.Enums;
using BambuMan.Shared.Scale;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
#if ANDROID
using BambuMan.Implementations;
#endif

namespace BambuMan
{
    /// <summary>
    /// <see cref="IScaleService"/> over Plugin.BLE. A singleton: it keeps the chosen scale connected for the app's
    /// lifetime, whichever page is shown, and reconnects whenever the scale comes back in reach.
    /// </summary>
    public partial class BluetoothScaleService : ObservableObject, IScaleService
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

        private readonly ILogger<BluetoothScaleService> logger;

        // Android needs BLE commands one after the other; a second one started while another runs fails with a GATT error.
        private readonly SemaphoreSlim gate = new(1, 1);

        // Released to cut a reconnect wait short, when Bluetooth is turned back on.
        private readonly SemaphoreSlim wake = new(0);

        private bool subscribed;
        private CancellationTokenSource? run;
        private Task? runTask;

        // Valid for one connection only: services and characteristics are stale once the scale disconnects.
        private IDevice? device;
        private ICharacteristic? weightCharacteristic;
        private ICharacteristic? controlCharacteristic;
        private TaskCompletionSource? connectionLost;

        #region Properties

        [ObservableProperty] private ScaleConnectionState state;
        [ObservableProperty] private Guid? deviceId;
        [ObservableProperty] private string? deviceName;
        [ObservableProperty] private ScaleProtocol? protocol;
        [ObservableProperty] private ScaleReading? lastReading;
        [ObservableProperty] private bool canTare;
        [ObservableProperty] private bool isScanning;

        #endregion

        public BluetoothScaleService(ILogger<BluetoothScaleService> logger)
        {
            this.logger = logger;
        }

        public event Action<ScaleReading>? ReadingReceived;

        // Created on first use, so a user who never turns the scale on never starts the Bluetooth stack.
        private static IBluetoothLE Ble => CrossBluetoothLE.Current;

        private static IAdapter Adapter => Ble.Adapter;

        #region Permissions

        public async Task<bool> RequestPermissionsAsync()
        {
#if ANDROID
            var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<BluetoothScalePermission>);
            return status == PermissionStatus.Granted;
#else
            return await Task.FromResult(true);
#endif
        }

        private static async Task<bool> HasPermissionsAsync()
        {
#if ANDROID
            return await Permissions.CheckStatusAsync<BluetoothScalePermission>() == PermissionStatus.Granted;
#else
            return await Task.FromResult(true);
#endif
        }

        #endregion

        #region Scan

        public async Task<bool> ScanAsync(Action<ScaleDevice> found, CancellationToken cancellationToken)
        {
            if (IsScanning || !await HasPermissionsAsync() || !Ble.IsOn) return false;

            void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
            {
                try
                {
                    found(new ScaleDevice(e.Device.Id, string.IsNullOrWhiteSpace(e.Device.Name) ? null : e.Device.Name, e.Device.Rssi));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in OnDeviceDiscovered");
                }
            }

            IsScanning = true;
            Adapter.DeviceDiscovered += OnDeviceDiscovered;

            try
            {
                Adapter.ScanTimeout = (int)ScanDuration.TotalMilliseconds;
                Adapter.ScanMode = ScanMode.LowLatency;

                // Only scales advertising one of the two services are reported. A cancelled scan returns, it doesn't throw.
                await Adapter.StartScanningForDevicesAsync(new ScanFilterOptions { ServiceUuids = ScaleUuids.ScaleServices }, cancellationToken: cancellationToken);
                return true;
            }
            finally
            {
                Adapter.DeviceDiscovered -= OnDeviceDiscovered;
                IsScanning = false;
            }
        }

        #endregion

        #region Connection

        public async Task StartAsync(Guid id, string? name)
        {
            await StopAsync();

            Subscribe();

            DeviceId = id;
            DeviceName = name;

            run = new CancellationTokenSource();
            runTask = RunAsync(id, run.Token);
        }

        public async Task StopAsync()
        {
            if (run != null)
            {
                await run.CancelAsync();

                try
                {
                    if (runTask != null) await runTask;
                }
                finally
                {
                    run.Dispose();
                    run = null;
                    runTask = null;
                }
            }

            DeviceId = null;
            DeviceName = null;
            LastReading = null;
            State = ScaleConnectionState.Off;
        }

        /// <summary>Connects, waits for the connection to drop, and reconnects with a growing pause, until stopped.</summary>
        private async Task RunAsync(Guid id, CancellationToken cancellationToken)
        {
            var failures = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!await HasPermissionsAsync())
                    {
                        State = ScaleConnectionState.NoPermission;
                        return;
                    }

                    if (!Ble.IsOn) State = ScaleConnectionState.BluetoothOff;
                    else
                    {
                        State = failures == 0 ? ScaleConnectionState.Connecting : ScaleConnectionState.Reconnecting;

                        var lost = await ConnectAsync(id, cancellationToken);
                        if (lost == null)
                        {
                            State = ScaleConnectionState.NotAScale;
                            return;
                        }

                        failures = 0;
                        State = ScaleConnectionState.Connected;

                        await lost.WaitAsync(cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e) when (e is DeviceConnectionException or OperationCanceledException)
                {
                    // Out of reach or turned off (or the connect timed out): expected, wait for it to come back.
                    logger.LogDebug("Scale {Id} not reached: {Message}", id, e.Message);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Scale {Id} connection failed", id);
                }
                finally
                {
                    await CloseConnectionAsync();
                }

                if (cancellationToken.IsCancellationRequested) return;

                failures++;
                if (State == ScaleConnectionState.Connected) State = ScaleConnectionState.Reconnecting;

                try
                {
                    await wake.WaitAsync(RetryDelay(failures), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>2, 4, 8, 16, then every 30 seconds.</summary>
        private static TimeSpan RetryDelay(int failures) => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Min(failures, 5)), MaxRetryDelay.TotalSeconds));

        /// <summary>Connects and subscribes to the weight; the task returned completes when the connection drops. Null: not a scale.</summary>
        private async Task<Task?> ConnectAsync(Guid id, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);

            try
            {
                // Before connecting, so a drop right after the connect isn't missed.
                connectionLost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(ConnectTimeout);
                    device = await Adapter.ConnectToKnownDeviceAsync(id, new ConnectParameters(autoConnect: false, forceBleTransport: true), timeout.Token);
                }

                var bambuMan = await device.GetServiceAsync(ScaleUuids.BambuManService, cancellationToken);
                if (bambuMan != null)
                {
                    Protocol = ScaleProtocol.BambuMan;
                    weightCharacteristic = await bambuMan.GetCharacteristicAsync(ScaleUuids.BambuManWeight, cancellationToken);
                    controlCharacteristic = await bambuMan.GetCharacteristicAsync(ScaleUuids.BambuManControl, cancellationToken);
                }
                else if (await device.GetServiceAsync(ScaleUuids.WeightScaleService, cancellationToken) is { } weightScale)
                {
                    Protocol = ScaleProtocol.WeightScaleService;
                    weightCharacteristic = await weightScale.GetCharacteristicAsync(ScaleUuids.WeightMeasurement, cancellationToken);
                }

                if (weightCharacteristic is not { CanUpdate: true })
                {
                    logger.LogWarning("Device {Name} has no scale weight characteristic", device.Name);
                    return null;
                }

                CanTare = controlCharacteristic?.CanWrite == true;

                weightCharacteristic.ValueUpdated += OnWeightUpdated;
                await weightCharacteristic.StartUpdatesAsync(cancellationToken);

                return connectionLost.Task;
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task CloseConnectionAsync()
        {
            if (weightCharacteristic != null) weightCharacteristic.ValueUpdated -= OnWeightUpdated;

            weightCharacteristic = null;
            controlCharacteristic = null;
            connectionLost = null;
            CanTare = false;
            Protocol = null;

            var connected = device;
            device = null;

            if (connected == null) return;

            try
            {
                // Also frees the GATT client of a connect that failed half way, so the next attempt starts clean.
                using var timeout = new CancellationTokenSource(ConnectTimeout);
                await Adapter.DisconnectDeviceAsync(connected, timeout.Token);
            }
            catch (Exception e)
            {
                logger.LogDebug("Scale disconnect: {Message}", e.Message);
            }
        }

        #endregion

        #region Tare

        public async Task<bool> TareAsync()
        {
            var control = controlCharacteristic;
            if (control is not { CanWrite: true }) return false;

            await gate.WaitAsync();

            try
            {
                // Plugin.BLE: writes from another thread fail with a GATT write error on Android.
                await MainThread.InvokeOnMainThreadAsync(() => control.WriteAsync([BambuManScaleDecoder.TareCommand]));
                return true;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Scale tare failed");
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        #endregion

        #region Events

        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;

            // For the app's lifetime: this service is a singleton.
            Adapter.DeviceConnectionLost += OnDeviceConnectionLost;
            Ble.StateChanged += OnBluetoothStateChanged;
        }

        private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
        {
            if (e.Device?.Id == device?.Id) connectionLost?.TrySetResult();
        }

        private void OnBluetoothStateChanged(object? sender, BluetoothStateChangedArgs e)
        {
            if (e.NewState == BluetoothState.On && wake.CurrentCount == 0) wake.Release();
            else if (e.NewState != BluetoothState.On) connectionLost?.TrySetResult();
        }

        private void OnWeightUpdated(object? sender, CharacteristicUpdatedEventArgs e)
        {
            try
            {
                if (Protocol is not { } current) return;

                if (!ScaleReading.TryDecode(current, e.Characteristic.Value, DateTime.UtcNow, out var reading)) return;

                LastReading = reading;
                ReadingReceived?.Invoke(reading!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OnWeightUpdated");
            }
        }

        #endregion
    }
}
