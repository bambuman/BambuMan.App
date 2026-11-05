using BambuMan.Components.Common;
using BambuMan.Interfaces;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
using BambuMan.Shared.Resolvers;
using BambuMan.Shared.Scale;
using BambuMan.Shared.Services;
using BambuMan.UI.Main;
using BambuMan.UI.Settings;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan
{
    /// <summary>
    /// Wires NFC and the inventory backends to the scan page state. A singleton rather than page code, because Blazor
    /// pages are rebuilt on every navigation while tag reads and backend events must keep flowing into the same state.
    /// </summary>
    public class ScannerService
    {
        private readonly MainPageViewModel viewModel;
        private readonly IInventoryBackendResolver backends;
        private readonly ILogger<ScannerService> logger;
        private readonly IToneGenerator? toneGenerator;
        private readonly IInvokeIndent invokeIndent;
        private readonly TagApiService tagApiService;
        private readonly IFilamentOverrideService filamentOverrides;
        private readonly ToastService toasts;
        private readonly IPendingScanQueue pendingScans;
        private readonly IScaleService scale;

        // The scale reading last put in an edit card, and the tray uid of the spool it weighed.
        private (ScaleReading Reading, string? TrayUid)? weighedSpool;

#if !GOOGLE_PLAY
        private bool versionChecked;
#endif

        public ScannerService(MainPageViewModel viewModel, IInventoryBackendResolver backends, ILogger<ScannerService> logger, IToneGenerator toneGenerator, IInvokeIndent invokeIndent, TagApiService tagApiService, IFilamentOverrideService filamentOverrides, ToastService toasts, IPendingScanQueue pendingScans, IScaleService scale)
        {
            this.viewModel = viewModel;
            this.backends = backends;
            this.logger = logger;
            this.toneGenerator = toneGenerator;
            this.invokeIndent = invokeIndent;
            this.tagApiService = tagApiService;
            this.filamentOverrides = filamentOverrides;
            this.toasts = toasts;
            this.pendingScans = pendingScans;
            this.scale = scale;

            this.filamentOverrides.LogAction = async void (level, message) =>
            {
                try
                {
                    await viewModel.AddLog(level, message);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in FilamentOverrideService");
                }
            };
            this.tagApiService.LogAction = async void (level, message) =>
            {
                try
                {
                    await viewModel.AddLog(level, message);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in TagApiService");
                }
            };

            // Subscribed for the app's lifetime: this service is a singleton, so there is no page to unsubscribe with.
            foreach (var manager in backends.All)
            {
                manager.OnStatusChanged += ManagerOnStatusChanged;
                manager.OnLogMessage += ManagerOnLogMessage;
                manager.OnShowMessage += ManagerOnShowMessage;
                manager.OnPlayErrorTone += ManagerOnPlayErrorTone;
                manager.OnSpoolFound += ManagerOnSpoolFound;
                manager.OnSpoolInfoRead += ManagerOnSpoolInfoRead;
                manager.OnLocationsLoaded += ManagerOnLocationsLoaded;
            }

            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            NoBackend.OnSpoolsChanged += NoBackendOnSpoolsChanged;
            pendingScans.OnChanged += PendingScansOnChanged;
            scale.ReadingReceived += ScaleOnReadingReceived;
        }

        /// <summary>Raised with true when a read spool should take keyboard focus, false when the edit card closes.</summary>
        public event Action<bool>? SpoolEditFocusRequested;

        public MainPageViewModel ViewModel => viewModel;

        public BaseManager ActiveManager => backends.Resolve(SettingsPage.GetInventoryBackend());

        private NoBackendManager NoBackend => (NoBackendManager)backends.Resolve(InventoryBackend.NoBackend);

        /// <summary>
        /// Called whenever the scan page is shown: re-reads settings (they may have just changed), connects the
        /// selected backend and (re)starts NFC listening.
        /// </summary>
        public async Task ActivateAsync()
        {
            await ApplySettingsAsync();
            await LoadPendingScansAsync();
            await ApplyScaleSettingsAsync();
            await InitializeBackendsAsync();
            await SetupNfcAsync();
            await RefreshFilamentOverridesAsync().ConfigureAwait(false);
#if !GOOGLE_PLAY
            if (!versionChecked)
            {
                versionChecked = true;
                await CheckVersion().ConfigureAwait(false);
            }
#endif
        }

        #region Manager events

        private async void ManagerOnPlayErrorTone()
        {
            try
            {
                // A queued scan rejected in the background isn't the spool in hand; the summary toast reports it.
                if (IsSendingQueuedScan) return;

                if (toneGenerator != null) await toneGenerator.PlayAlarmTone();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in ManagerOnPlayErrorTone");
            }
        }

        private void ManagerOnSpoolFound(SpoolFound found, ISpoolTag tag)
        {
            // Read now: the send is over by the time the main thread runs the code below.
            var queued = IsSendingQueuedScan;

            // On the main thread, so the inventory counter is updated before HasInventoryItems is read back from it.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    viewModel.InventorySpool(found, tag);

                    // A queued scan sent in the background only counts; the edit card is for the spool just read.
                    // A backend that can't save edits shows the info card from OnSpoolInfoRead instead.
                    if (queued || !ActiveManager.CanEditSpools) return;

                    // A spool added by hand was saved from its own edit card already; don't open it again. A card still
                    // open from an earlier scan is closed: the manager's current spool is now the one added by hand, so
                    // saving that card would write to the wrong spool.
                    if (!tag.HasTag)
                    {
                        viewModel.ShowSpoolEdit = false;
                        return;
                    }

                    viewModel.ShowSpool(found, tag);

                    // A spool put on the scale before its tag is read: the latest reading is its weight — unless it
                    // already weighed another spool.
                    var weighedAnotherSpool = weighedSpool is { } weighed && weighed.TrayUid != found.TrayUid ? weighed.Reading : null;
                    FillWeightFromScale(ScaleWeight.ForSpoolRead(scale.LastReading, DateTime.UtcNow, weighedAnotherSpool));

                    if (viewModel.ShowKeyboardOnSpoolRead) SpoolEditFocusRequested?.Invoke(true);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in ManagerOnSpoolFound");
                }
            });
        }

        private void ManagerOnSpoolInfoRead(SpoolDisplayInfo info)
        {
            try
            {
                // Where the spool can be edited, the edit card from OnSpoolFound takes its place.
                if (ActiveManager.CanEditSpools) return;

                viewModel.ShowReadOnlySpool(info);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in ManagerOnSpoolInfoRead");
            }
        }

        private async void ManagerOnShowMessage(bool isError, string message)
        {
            try
            {
                // Each queued scan sent in the background would replace the banner; the summary toast reports them.
                if (!isError && IsSendingQueuedScan) return;

                await viewModel.ClearMessages();

                if (isError)
                {
                    await viewModel.ShowErrorMessage(message);
                    toasts.Show(message, ToastKind.Error, true);
                }
                else await viewModel.ShowSuccessMessage(message);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in ManagerOnShowMessage");
            }
        }

        private async void ManagerOnLogMessage(LogLevel level, string message)
        {
            try
            {
                await viewModel.AddLog(level, message);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in ManagerOnLogMessage");
            }
        }

        private async void ManagerOnStatusChanged()
        {
            try
            {
                var manager = ActiveManager;

                if (viewModel.SpoolmanConnecting && manager.Status == ManagerStatusType.Ready) await viewModel.ShowInfoMessage("Ready to read spools.");

                if (manager.Status >= ManagerStatusType.Ready) viewModel.SpoolmanConnecting = false;

                viewModel.BackendStatus = manager.Status;

                await viewModel.Validate(manager);

                // Connected (again): send what was queued while the backend couldn't be reached.
                if (pendingScans.HasScansToSend(manager)) _ = SendQueuedScansAsync(manual: false, x => pendingScans.SendAsync(x));
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in ManagerOnStatusChanged");
            }
        }

        private void ManagerOnLocationsLoaded()
        {
            viewModel.ExistingLocations = ActiveManager.ExistingLocations;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainPageViewModel.OverrideLocationOnRead))
                foreach (var manager in backends.All) manager.OverrideLocationOnRead = viewModel.OverrideLocationOnRead;
        }

        #endregion

        #region Backends

        private async Task ApplySettingsAsync()
        {
            try
            {
                viewModel.ShowSpoolEdit = false;
                viewModel.ShowSpoolInfo = false;

                viewModel.ShowLogsOnMainPage = Preferences.Default.Get(SettingsPage.ShowLogsOnMainPage, true);
                viewModel.ShowKeyboardOnSpoolRead = Preferences.Default.Get(SettingsPage.ShowKeyboardOnSpoolRead, true);
                viewModel.FullTagScanAndUpload = Preferences.Default.Get(SettingsPage.FullTagScanAndUpload, false);

                var active = ActiveManager;
                var activeChanged = ConfigureBackends(active);

                viewModel.OverrideLocationOnRead = active.OverrideLocationOnRead;
                viewModel.ExistingLocations = active.ExistingLocations;
                viewModel.Backend = active.Backend;
                viewModel.BackendLabel = active.Backend.DisplayName();
                viewModel.BackendStatus = active.Status;
                UpdateEditCapabilities(active);
                viewModel.ShowLocalSpools = active.Backend == InventoryBackend.NoBackend;
                UpdatePendingScans();

                // Only show connecting animation if the active backend's config changed or it's not yet initialized
                if (activeChanged || !active.IsInitialized)
                {
                    viewModel.SpoolmanOk = false;
                    viewModel.SpoolmanConnecting = true;
                }

                await viewModel.Validate(active);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in scan page setup");
            }
        }

        /// <summary>Push current settings onto every backend; returns whether the ACTIVE backend's config changed (URL/key).</summary>
        private bool ConfigureBackends(BaseManager active)
        {
            var unknownFilamentEnabled = Preferences.Default.Get(SettingsPage.UnknownFilamentEnabled, true);
            bool HasNetwork() => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            var activeChanged = false;

            foreach (var manager in backends.All)
            {
                manager.AppVersion = BuildVersionModel.CurrentBuildVersion;
                manager.ShowLogs = true;
                manager.UnknownFilamentEnabled = unknownFilamentEnabled;
                manager.HasNetworkAccess = HasNetwork;
                manager.FilamentOverrides = filamentOverrides;

                var changed = false;

                switch (manager.Backend)
                {
                    case InventoryBackend.Spoolman:
                        var spoolmanUrl = Preferences.Default.Get(SettingsPage.KeySpoolmanUrl, string.Empty);
                        changed = spoolmanUrl != manager.ApiUrl;
                        manager.ApiUrl = spoolmanUrl;
                        break;

                    case InventoryBackend.Bambuddy when manager is BambuddyManager bambuddy:
                        var bambuddyUrl = Preferences.Default.Get(SettingsPage.KeyBambuddyUrl, string.Empty);
                        var bambuddyApiKey = Preferences.Default.Get(SettingsPage.KeyBambuddyApiKey, string.Empty);
                        changed = bambuddyUrl != bambuddy.ApiUrl || bambuddyApiKey != bambuddy.ApiKey;
                        bambuddy.ApiUrl = bambuddyUrl;
                        bambuddy.ApiKey = bambuddyApiKey;
                        if (changed) bambuddy.ResetInitialization();
                        break;

                    case InventoryBackend.NoBackend when manager is NoBackendManager noBackend:
                        // No url, no key — only whether scanned spools are kept on the device. Applied on its next Init.
                        noBackend.StoreSpoolsLocally = Preferences.Default.Get(SettingsPage.KeyStoreSpoolsLocally, false);
                        break;
                }

                if (manager.Backend == active.Backend) activeChanged = changed;
            }

            return activeChanged;
        }

        /// <summary>
        /// Connect the selected backend only. The others are configured and subscribed, but must not
        /// connect, log, or poll — a backend the user did not select never talks to its server.
        /// </summary>
        private async Task InitializeBackendsAsync()
        {
            var active = ActiveManager;

            foreach (var manager in backends.All)
            {
                if (manager.Backend != active.Backend) manager.StopHealthChecks();
            }

            try
            {
                await active.Init();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error initializing {Backend}", active.Backend);
            }

            _ = active.RefreshLocationsAsync();

            // Spools stored while no backend was selected stay on the device with a server selected too, so they can be
            // sent there; load them for the menu and the spool list.
            if (active.Backend != InventoryBackend.NoBackend)
            {
                try
                {
                    await PrepareLocalInventoryAsync();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error loading stored spools");
                }
            }

            // The device store only loads during Init, and whether spools can be edited depends on it.
            UpdateEditCapabilities(active);
            UpdateLocalSpools();
        }

        private void UpdateEditCapabilities(BaseManager active)
        {
            viewModel.ShowInventoryOptions = active.CanEditSpools;
            viewModel.EditFields = active.EditFields;
            viewModel.ExistingLocations = active.ExistingLocations;
        }

        /// <summary>
        /// Makes sure the no-backend spools reflect the "store spools locally" setting, for the spool pages, which can
        /// be opened straight from settings before the scan page has re-applied it. With a server selected only the
        /// stored spools are loaded: the no-backend mode isn't started, so it doesn't log or report itself ready.
        /// </summary>
        public async Task PrepareLocalInventoryAsync()
        {
            var noBackend = NoBackend;
            var storeLocally = Preferences.Default.Get(SettingsPage.KeyStoreSpoolsLocally, false);

            noBackend.StoreSpoolsLocally = storeLocally;

            if (ActiveManager != noBackend)
            {
                if (noBackend.IsStoreLoaded != storeLocally) await noBackend.LoadStoreAsync();
            }
            else if (!noBackend.IsInitialized || noBackend.IsStoreLoaded != storeLocally) await noBackend.Init();
        }

        #endregion

        #region Scale

        /// <summary>Connects the scale chosen in settings, or lets it go. Called on every scan page visit, after settings may have changed.</summary>
        private async Task ApplyScaleSettingsAsync()
        {
            try
            {
                await SettingsPage.ApplyScaleSettingsAsync(scale);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error applying the Bluetooth scale settings");
            }
        }

        private void ScaleOnReadingReceived(ScaleReading reading)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    // A spool put on the scale after its tag was read, or one still settling.
                    if (viewModel.ShowSpoolEdit) FillWeightFromScale(reading);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in ScaleOnReadingReceived");
                }
            });
        }

        /// <summary>
        /// Puts a scale reading in the edit card's weight. Not once the user typed a weight of their own, and a live
        /// reading never replaces a stable one (see <see cref="ScaleWeight.ShouldReplace"/>).
        /// </summary>
        private void FillWeightFromScale(ScaleReading? reading)
        {
            var edit = viewModel.SpoolEdit;

            if (reading == null || edit.WeightTyped || !ScaleWeight.ShouldReplace(viewModel.AppliedScaleReading, reading)) return;

            edit.Weight = ScaleWeight.Round(reading.Grams);
            weighedSpool = (reading, viewModel.Spool?.TrayUid);

            // Last: the scan page re-renders on it, with the new weight already in place.
            viewModel.AppliedScaleReading = reading;
        }

        #endregion

        #region Nfc Logic

        private async Task SetupNfcAsync()
        {
            try
            {
                // In order to support Mifare Classic 1K tags (read/write), you must set legacy mode to true.
                CrossNfc.Legacy = false;

                if (CrossNfc.IsSupported)
                {
                    viewModel.NfcIsEnabled = CrossNfc.Current.IsEnabled;
                    viewModel.NfcText = CrossNfc.Current.IsEnabled ? "Enabled" : "Disabled";

                    await viewModel.Validate(ActiveManager);

                    if (DeviceInfo.Platform == DevicePlatform.iOS) viewModel.IsDeviceOs = true;

                    await AutoStartAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in NFC setup");
            }
        }

        /// <summary>
        /// Display an alert
        /// </summary>
        /// <param name="message">Message to be displayed</param>
        /// <param name="title">Alert title</param>
        /// <returns>The task to be performed</returns>
        private static Task ShowAlert(string message, string? title = null) =>
            MainThread.InvokeOnMainThreadAsync(() => Application.Current?.Windows.FirstOrDefault()?.Page?.DisplayAlertAsync(string.IsNullOrWhiteSpace(title) ? "NFC" : title, message, "OK") ?? Task.CompletedTask);

        /// <summary>
        /// Task to start listening for NFC tags if the user's device platform is not iOS
        /// </summary>
        /// <returns>The task to be performed</returns>
        private async Task StartListeningIfNotiOs()
        {
            if (viewModel.IsDeviceOs)
            {
                SubscribeEvents();
                return;
            }

            await BeginListening();
        }

        /// <summary>
        /// Task to safely start listening for NFC Tags
        /// </summary>
        /// <returns>The task to be performed</returns>
        private async Task BeginListening()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SubscribeEvents();
                    CrossNfc.Current.StartListening();
                });
            }
            catch (Exception ex)
            {
                await ShowAlert(ex.Message);
            }
        }

        /// <summary>
        /// Auto Start Listening
        /// </summary>
        /// <returns></returns>
        private async Task AutoStartAsync()
        {
            // Some delay to prevent Java.Lang.IllegalStateException "Foreground dispatch can only be enabled when your activity is resumed" on Android
            await Task.Delay(500);
            await StartListeningIfNotiOs();
        }

        /// <summary>
        /// Subscribe to the NFC events
        /// </summary>
        private void SubscribeEvents()
        {
            if (viewModel.EventsAlreadySubscribed) UnsubscribeEvents();

            viewModel.EventsAlreadySubscribed = true;

            CrossNfc.Current.FullTagScanAndUpload = viewModel.FullTagScanAndUpload;
            CrossNfc.Current.OnMessageReceived += Current_OnMessageReceived;
            CrossNfc.Current.OnNfcStatusChanged += Current_OnNfcStatusChanged;
            CrossNfc.Current.OnTagListeningStatusChanged += Current_OnTagListeningStatusChanged;
            CrossNfc.Current.OnTagIntentReceived += Current_OnTagIntentReceived;

            if (viewModel.IsDeviceOs) CrossNfc.Current.OnIOsReadingSessionCancelled += Current_OniOSReadingSessionCancelled;
        }

        /// <summary>
        /// Unsubscribe from the NFC events
        /// </summary>
        private void UnsubscribeEvents()
        {
            CrossNfc.Current.OnMessageReceived -= Current_OnMessageReceived;
            CrossNfc.Current.OnNfcStatusChanged -= Current_OnNfcStatusChanged;
            CrossNfc.Current.OnTagListeningStatusChanged -= Current_OnTagListeningStatusChanged;
            CrossNfc.Current.OnTagIntentReceived -= Current_OnTagIntentReceived;

            if (viewModel.IsDeviceOs) CrossNfc.Current.OnIOsReadingSessionCancelled -= Current_OniOSReadingSessionCancelled;

            viewModel.EventsAlreadySubscribed = false;
        }

        private void Current_OnTagIntentReceived(object? sender, EventArgs e) => CloseSpool();

        /// <summary>
        /// Event raised when Listener Status has changed
        /// </summary>
        /// <param name="isListening"></param>
        private void Current_OnTagListeningStatusChanged(bool isListening) => viewModel.DeviceIsListening = isListening;

        /// <summary>
        /// Event raised when NFC Status has changed
        /// </summary>
        /// <param name="isEnabled">NFC status</param>
        private async void Current_OnNfcStatusChanged(bool isEnabled)
        {
            try
            {
                viewModel.NfcIsEnabled = isEnabled;
                viewModel.NfcText = isEnabled ? "Enabled" : "Disabled";

                await viewModel.Validate(ActiveManager);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error on Current_OnNfcStatusChanged");
            }
        }

        /// <summary>
        /// Event raised when a NDEF message is received
        /// </summary>
        /// <param name="tagInfo">Received <see cref="ITagInfo"/></param>
        private async void Current_OnMessageReceived(ITagInfo? tagInfo)
        {
            try
            {
                SentrySdk.Metrics.EmitCounter("nfc.tag.read", 1);

                if (tagInfo == null)
                {
                    SentrySdk.Metrics.EmitCounter("nfc.tag.read.failure", 1);
                    await ShowAlert("No tag found");
                    return;
                }

                // Customized serial number
                var identifier = tagInfo.Identifier;
                var serialNumber = NfcUtils.ByteArrayToHexString(identifier, ":");
                var title = !string.IsNullOrWhiteSpace(serialNumber) ? $"Tag [{serialNumber}]" : "Tag Info";

                if (tagInfo is BambuFilamentInfo bambuFilamentInfo)
                {
#if DEBUG
                    await viewModel.AddLog(LogLevel.Information, $"Nfc read time: {bambuFilamentInfo.ReadTime:0.###}ms");
#endif

                    var json = JsonConvert.SerializeObject(bambuFilamentInfo, Formatting.Indented);
                    await viewModel.AddLog(LogLevel.Information, json);

                    await InventorySpoolAsync(bambuFilamentInfo);
                    return;
                }

                // NTAG-based formats (OpenSpool, TigerTag) arrive as NDEF records / raw user memory and are decoded here.
                if (SpoolTagReader.TryRead(tagInfo) is SpoolTag spoolTag)
                {
                    await viewModel.AddLog(LogLevel.Information, $"{spoolTag.Format.DisplayName()} tag: {JsonConvert.SerializeObject(spoolTag, Formatting.Indented)}");

                    await InventorySpoolAsync(spoolTag);
                    return;
                }

                if (tagInfo is MifareReadFailure failure)
                {
                    SentrySdk.Metrics.EmitCounter("nfc.tag.read.failure", 1);

                    // Counts and the phone's MIFARE feature flag only, so the logs tell an unsupported phone from a
                    // tag moved away mid-read. No keys or tag ids.
                    logger.LogWarning("MIFARE read failed ({Reason}): {SectorsAuthenticated}/{SectorCount} sectors authenticated, {BlocksRead} blocks read, com.nxp.mifare: {HasMifareFeature}",
                        failure.Reason, failure.SectorsAuthenticated, failure.SectorCount, failure.BlocksRead, failure.HasMifareFeature);
                    await viewModel.AddLog(LogLevel.Warning, failure.ToString());

                    await viewModel.ShowErrorMessage(failure.IsAuthenticationUnsupported
                        ? "This phone couldn't unlock the tag. Some phones' NFC chips (e.g. Google Pixel) can't read Bambu Lab (MIFARE Classic) tags."
                        : "Tag read was interrupted, hold the phone still on the tag and try again.");

                    if (toneGenerator != null) await toneGenerator.PlayAlarmTone();
                }
                else if (!tagInfo.IsSupported)
                {
                    SentrySdk.Metrics.EmitCounter("nfc.tag.read.failure", 1);
                    await viewModel.ShowErrorMessage("Error reading tag, please try again!");
                    if (toneGenerator != null) await toneGenerator.PlayAlarmTone();
                }
                else if (tagInfo.IsEmpty)
                {
                    SentrySdk.Metrics.EmitCounter("nfc.tag.read.failure", 1);
                    await viewModel.ShowErrorMessage("Empty tag");
                }
                else if (tagInfo.Records is { Length: > 0 })
                {
                    var first = tagInfo.Records.FirstOrDefault(x => x != null);
                    if (first != null) await ShowAlert(GetMessage(first), title);
                }
            }
            catch (Exception e)
            {
                SentrySdk.Metrics.EmitCounter("nfc.tag.read.failure", 1);
                logger.LogError(e, "Error in Current_OnMessageReceived");
            }
        }

        /// <summary>
        /// Inventory a read tag against the active backend with the configured defaults, then upload it if opted in.
        /// Only Bambu Lab tags are uploaded: the upload consent covers those, and the tag database is built from them.
        /// </summary>
        private async Task InventorySpoolAsync(ISpoolTag tag)
        {
            LastReadTag = tag;

            var (buyDate, defaultPrice, defaultLotNr, defaultLocation) = GetSpoolDefaults();

            await viewModel.ClearMessages();

            // Sending goes through the same backend calls and leaves the sent spool as the one an edit is saved to, so a
            // scan in between would mix the two up.
            if (IsSendingSpools)
            {
                await viewModel.ShowErrorMessage("Stored spools are being sent. Scan again once that's done.");
                toasts.Show("Stored spools are being sent. Scan again once that's done.", ToastKind.Error);
                return;
            }

            var active = ActiveManager;

            // Settings hides the defaults when read spools are only shown (no backend, not stored), so they must not
            // apply unseen either.
            if (active is NoBackendManager { CanEditSpools: false })
            {
                buyDate = null;
                defaultPrice = null;
                defaultLotNr = null;
                defaultLocation = null;
            }

            SentrySdk.ConfigureScope(s => s.SetTag("inventory.backend", active.Backend.ToString()));

            var scan = new PendingScan { Backend = active.Backend, Tag = tag, BuyDate = buyDate, Price = defaultPrice, LotNr = defaultLotNr, Location = defaultLocation };
            var sendResult = await pendingScans.SendOrQueueAsync(active, scan);

            if (sendResult.Status == ScanSendStatus.Unreachable) await ShowScanQueuedAsync(active);

            if (viewModel.FullTagScanAndUpload && tag is BambuFilamentInfo info)
            {
                var (_, rateLimited) = await tagApiService.UploadNfcTagAsync(info);
                if (rateLimited)
                {
                    await viewModel.ShowErrorMessage("Daily upload limit reached (1000 tags/day). Try again tomorrow.");
                }
            }
        }

        /// <summary>
        /// Event raised when user cancelled NFC session on iOS
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Current_OniOSReadingSessionCancelled(object? sender, EventArgs e) => System.Diagnostics.Debug.WriteLine("iOS NFC Session has been cancelled");

        /// <summary>
        /// Returns the tag information from NDEF record
        /// </summary>
        /// <param name="record"><see cref="NfcNdefRecord"/></param>
        /// <returns>The tag information</returns>
        private static string GetMessage(NfcNdefRecord record)
        {
            var message = $"Message: {record.Message}";
            message += Environment.NewLine;
            message += $"RawMessage: {Encoding.UTF8.GetString(record.Payload ?? [])}";
            message += Environment.NewLine;
            message += $"Type: {record.TypeFormat}";

            if (string.IsNullOrWhiteSpace(record.MimeType)) return message;

            message += Environment.NewLine;
            message += $"MimeType: {record.MimeType}";

            return message;
        }

        #endregion

        #region Queued scans

        /// <summary>
        /// True while a backend is sending a scan from the queue rather than one just read. Any backend, not just the
        /// active one: the selection can change while a send to the previous one is still running.
        /// </summary>
        private bool IsSendingQueuedScan => backends.All.Any(x => x.SendingScan?.IsQueued == true);

        /// <summary>Reads the queued scans from the device (once). A damaged file is reported and left alone, never saved over.</summary>
        public async Task LoadPendingScansAsync()
        {
            try
            {
                await pendingScans.LoadAsync();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error loading queued scans");
                toasts.Show("Could not read the queued scans. See logs.", ToastKind.Error, true);
            }

            UpdatePendingScans();
        }

        /// <summary>Tells the user the spool just read was queued, not lost.</summary>
        private async Task ShowScanQueuedAsync(BaseManager manager)
        {
            var name = manager.Backend.DisplayName();

            // A healthy server whose setup hasn't finished (still connecting, or it failed — see logs) isn't "unreachable".
            var message = manager.IsHealth
                ? $"{name} isn't ready yet. The spool is queued and will be sent once it is."
                : $"{name} can't be reached. The spool is queued and will be sent when it's back.";

            await viewModel.ShowInfoMessage(message);
            toasts.Show(message, ToastKind.Info, true);
        }

        /// <summary>Sends every scan queued for the selected backend, the rejected ones too.</summary>
        public Task SendPendingScansAsync() => SendQueuedScansAsync(manual: true, x => pendingScans.SendAsync(x, retryRejected: true));

        /// <summary>Sends one queued scan, rejected before or not.</summary>
        public Task SendPendingScanAsync(Guid id) => SendQueuedScansAsync(manual: true, x => pendingScans.SendOneAsync(x, id));

        /// <summary>Sends queued scans to the active backend and reports the outcome. Only a send the user asked for reports a backend still unreachable.</summary>
        private async Task SendQueuedScansAsync(bool manual, Func<BaseManager, Task<PendingScanSendResult>> send)
        {
            try
            {
                // Another send (e.g. the one a reconnect started) would make this one return without doing anything.
                if (manual && pendingScans.IsSending)
                {
                    toasts.Show("Queued scans are already being sent.", ToastKind.Info);
                    return;
                }

                var active = ActiveManager;
                var name = active.Backend.DisplayName();
                var result = await send(active);

                if (result.Sent > 0)
                    toasts.Show(result.Sent == 1 ? $"Sent 1 queued scan to {name}." : $"Sent {result.Sent} queued scans to {name}.", ToastKind.Success, true);

                if (result.Rejected > 0)
                    toasts.Show(result.Rejected == 1 ? $"{name} rejected 1 queued scan. See Queued scans." : $"{name} rejected {result.Rejected} queued scans. See Queued scans.", ToastKind.Error, true);

                if (manual && result.Unreachable)
                    toasts.Show($"{name} still can't be reached. The scans stay queued.", ToastKind.Error, true);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error sending queued scans");
                toasts.Show("Sending the queued scans failed. See logs.", ToastKind.Error, true);
            }
        }

        private void PendingScansOnChanged() => UpdatePendingScans();

        private void UpdatePendingScans()
        {
            var scans = pendingScans.Items;
            var backend = ActiveManager.Backend;

            var waiting = scans.Count(x => x.Backend == backend && !x.IsRejected);
            var rejected = scans.Count(x => x.Backend == backend && x.IsRejected);
            var otherBackend = scans.Count(x => x.Backend != backend);

            var parts = new List<string>();

            if (waiting > 0) parts.Add($"Sent automatically when {backend.DisplayName()} is reachable again.");
            if (rejected > 0) parts.Add(rejected == 1 ? "1 was rejected, open the list to send it again or discard it." : $"{rejected} were rejected, open the list to send them again or discard them.");
            if (otherBackend > 0) parts.Add(otherBackend == 1 ? "1 is for another backend." : $"{otherBackend} are for another backend.");

            var count = scans.Count;
            var text = string.Join(" ", parts);
            var canSend = waiting + rejected > 0;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                viewModel.PendingScansCount = count;
                viewModel.PendingScansText = text;
                viewModel.CanSendPendingScans = canSend;
            });
        }

        #endregion

        #region Filament match overrides

        /// <summary>
        /// Ask the api whether it has newer per-SKU match corrections than this build. Best-effort: a failure
        /// leaves the compiled-in (or previously cached) set in place, so scanning is never blocked on it.
        /// </summary>
        private async Task RefreshFilamentOverridesAsync()
        {
            try
            {
                await filamentOverrides.RefreshAsync();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Error refreshing filament overrides");
            }
        }

        #endregion

        #region Check for new version

        private async Task CheckVersion()
        {
            await Task.Delay(500);

            try
            {
                // ReSharper disable once ShortLivedHttpClient
                using var httpClient = new HttpClient();
                var request = await httpClient.GetAsync("https://api.github.com/repos/bambuman/BambuMan.App/releases/latest");
                var content = await request.Content.ReadAsStringAsync();

                dynamic data = JObject.Parse(content);

                var currentTagName = $"v{BuildVersionModel.CurrentBuildVersion}";
                string tagName = data["tag_name"]?.ToString() ?? currentTagName;

                viewModel.NewVersionAvailable = !tagName.Equals(currentTagName, StringComparison.CurrentCultureIgnoreCase);
                viewModel.NewVersionText = $"New version available: {tagName}";

                //In debug we should not show it hoggs screen
#if DEBUG
                viewModel.NewVersionAvailable = false;
#endif
            }
            catch (Exception)
            {
                //ignore
            }
        }

        #endregion

        #region Page actions

        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        public async Task TestTagAsync()
        {
            try
            {
                var dataLongitudinal = "2gTK38sIBAAE7+LThEa8kEEwMC1NMgAAR0ZBMDAAAABQTEEAAAAAAAAAAAAAAAAAj6I0TjpCh4eHaUzx56HVOFBMQSBCYXNpYwAAAAAAAAAwf+L/6AMAAAAA4D8AAAAANwAIAAAAAADmAL4AAAAAAGO3Tm5QLYeHh2lFGA4vcBmsDYgT6APoAwAAQD/NzEw+QtwFeFsbQSeIqVoWefuLPwAAAADhGQAAAAAAAAAAAAAchlMhA/KHh4dp5M9qkeR4MjAyNF8wNl8yNl8wOF8xNzI0XzA2XzI2XzA4AAAAAAAAAAAASgEAAAAAAAAAAAAAZykp5/PQh4eHaaS4zo243AIAAgD/m/9UAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAF+u9l44qoeHh2np51tuCSMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAev+pvMiaHh4dp0FCv+tkSAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAjmBJcuM/h4eHaVDa8TNsrQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAO1IFbR3oYeHh2lFtqszHLkAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADOyuVYGkOHh4dp8F4DumLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA9Xu6vCQ4h4eHaQWtYR9eEAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAATrA8lD3R5t0jd6l9tYpY9AN14fVR1YeHh2kXqMRrgkfi4KYOllLMnFzvTEsVv2DVwhBPYzFqob4v9uijC1sYsONVFLpPkoyTQtKu5eKg4j/0i/iVXBmHh4dpiCQmh8D8g6NlNRrsnW2c5em5/4IX0jYy2URgH5mMlblvGrIEyw5/Cy5G6bdXsqi2uLya80TrtaGVFt1Zh4eHaZNsNxt9+YvngMmocr4e46/FWgBLtOIlLyqmHe/n+KESZaoZc2Hf4vUxXG9wVIwZduYMrop9KUKFr9cno4eHh2mpYUno1c00VJYxBPuSw99xZZGxy11f/qaOLVREyiciL+aia5vM9FBMdYgqsQryW13mfBqeQgR9Ji9/K2eHh4dpuOhd3XqRxHCRSMsncC4gVoJCTKkOq5xS28st7HD/+twihntY+XSzpCV3hGQPD7TfeMvBnFWYG7DP1SWhh4eHad6EsAwkoQ==";

                var obj = new BambuFilamentInfo();
                obj.ParseData(Convert.FromBase64String(dataLongitudinal));

                var objJsonEnc = JsonConvert.SerializeObject(obj, Formatting.Indented);
                await viewModel.AddLog(LogLevel.Information, objJsonEnc);

                await InventorySpoolAsync(obj);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error on Test button click");
            }
        }

        public void CloseSpool()
        {
            viewModel.ShowSpoolEdit = false;
            viewModel.ShowSpoolInfo = false;

            SpoolEditFocusRequested?.Invoke(false);
        }

        /// <summary>Validates and writes the edit card back to the active backend; false leaves the card open with its errors.</summary>
        public async Task<bool> SaveSpoolAsync()
        {
            try
            {
                await viewModel.ClearMessages();

                if (!viewModel.SpoolEdit.Validate()) return false;

                await ActiveManager.UpdateCurrentSpoolAsync(viewModel.SpoolEdit.ToInput());

                CloseSpool();
                return true;
            }
            catch (Exception ex)
            {
                await viewModel.AddLog(LogLevel.Error, "Error on save changes. " + ex.Message);
                logger.LogError(ex, "Error on spool save changes");
                return false;
            }
        }

        public async Task ClearInventoryAsync()
        {
            try
            {
                viewModel.ClearInventory();

                // Without the device store the scanned spools are this session's batch, the same the counter shows,
                // so clearing it starts a fresh export too. Stored spools are only removed from the list page.
                if (ActiveManager is NoBackendManager { IsStoreLoaded: false } noBackend) await noBackend.ClearSpoolsAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ClearInventoryAsync");
            }
        }

        public async Task EmailLogsAsync()
        {
            try
            {
                var logs = $"{string.Join("\r\n", viewModel.Logs.Select(x => x.Content))}\r\n\r\n";
                await invokeIndent.SendEmail("bambuman.app@gmail.com", "Bambu logs", logs);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in EmailLogsAsync");
            }
        }

        #endregion

        #region Manual spools

        /// <summary>The spool defaults from settings, applied to a read spool and prefilled into a spool added by hand.</summary>
        private static (DateTime? BuyDate, decimal? Price, string? LotNr, string? Location) GetSpoolDefaults()
        {
            var buyDate = DateTime.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? (DateTime?)resultDate : null;
            var price = decimal.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? (decimal?)result : null;
            string? lotNr = Preferences.Default.Get(SettingsPage.KeyDefaultLotNr, string.Empty);
            string? location = Preferences.Default.Get(SettingsPage.KeyDefaultLocation, string.Empty);

            return (buyDate, price, lotNr, location);
        }

        /// <summary>
        /// Whether the active backend can take a spool added by hand: it must be able to save spools, and be connected.
        /// Not <see cref="BaseManager.Status"/>: a scan without a catalog match leaves that at Error while the server is fine.
        /// </summary>
        public bool CanAddManualSpool => ActiveManager is { CanEditSpools: true, IsInitialized: true, IsHealth: true };

        /// <summary>
        /// The edit card for a spool added by hand, prefilled with the settings defaults and weighed as a full spool:
        /// <paramref name="labelWeight"/> plus the empty spool.
        /// </summary>
        public SpoolEditModel NewManualSpoolEdit(int labelWeight, decimal? emptyWeight)
        {
            var (buyDate, price, lotNr, location) = GetSpoolDefaults();
            var empty = emptyWeight ?? BambuddyManager.CoreWeight;

            return new SpoolEditModel
            {
                Weight = labelWeight + empty,
                EmptyWeight = empty,
                Price = price,
                BuyDate = buyDate,
                LotNr = string.IsNullOrEmpty(lotNr) ? null : lotNr,
                Location = string.IsNullOrEmpty(location) ? null : location
            };
        }

        /// <summary>Validates the edit card and adds the spool to the active backend; false leaves the card open.</summary>
        public async Task<bool> AddManualSpoolAsync(ManualSpool spool, SpoolEditModel edit)
        {
            try
            {
                await viewModel.ClearMessages();

                if (!edit.Validate()) return false;

                // Sending leaves the sent spool as the current one; one added meanwhile would take its place.
                if (IsSendingSpools)
                {
                    await viewModel.ShowErrorMessage("Stored spools are being sent. Add the spool once that's done.");
                    return false;
                }

                var active = ActiveManager;

                SentrySdk.ConfigureScope(s => s.SetTag("inventory.backend", active.Backend.ToString()));

                return await active.AddManualSpoolAsync(spool, edit.ToInput());
            }
            catch (Exception ex)
            {
                await viewModel.AddLog(LogLevel.Error, "Error adding spool manually. " + ex.Message);
                logger.LogError(ex, "Error adding spool manually");
                return false;
            }
        }

        #endregion

        #region Local spools

        private void NoBackendOnSpoolsChanged() => UpdateLocalSpools();

        private void UpdateLocalSpools()
        {
            var noBackend = NoBackend;
            var stored = noBackend.IsStoreLoaded;
            var count = noBackend.Spools.Count;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                viewModel.LocalSpoolsStored = stored;
                viewModel.LocalSpoolsCount = count;
                viewModel.LocalSpoolsText = stored ? "Stored spools" : "Scanned spools";
            });
        }

        public async Task ExportCsvAsync()
        {
            try
            {
                var spools = NoBackend.Spools;

                if (spools.Count == 0)
                {
                    await viewModel.ShowInfoMessage("There are no spools to export yet.");
                    return;
                }

                var path = Path.Combine(FileSystem.CacheDirectory, LocalSpoolCsv.FileName(DateTime.Now));

                await using (var stream = File.Create(path)) LocalSpoolCsv.Write(stream, spools);

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Export spools",
                    File = new ShareFile(path, "text/csv")
                });
            }
            catch (Exception ex)
            {
                await viewModel.ShowErrorMessage("Export failed. See logs.");
                logger.LogError(ex, "Error in ExportCsvAsync");
            }
        }

        private static readonly FilePickerFileType CsvFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            // Android file managers label .csv inconsistently, so accept the usual spellings.
            { DevicePlatform.Android, ["text/csv", "text/comma-separated-values", "application/csv", "application/vnd.ms-excel", "text/plain"] },
            { DevicePlatform.iOS, ["public.comma-separated-values-text"] }
        });

        public async Task ImportCsvAsync()
        {
            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Import spools CSV", FileTypes = CsvFileType });

                if (file == null) return;

                LocalSpoolCsvReadResult result;

                await using (var stream = await file.OpenReadAsync()) result = LocalSpoolCsv.Read(stream);

                foreach (var error in result.Errors) await viewModel.AddLog(LogLevel.Warning, $"{file.FileName}: {error}");

                if (result.Spools.Count == 0)
                {
                    await viewModel.ShowErrorMessage($"No spools imported from {file.FileName}. {result.Errors.FirstOrDefault()}");
                    return;
                }

                var (added, updated) = await NoBackend.ImportSpoolsAsync(result.Spools);

                var message = $"Imported {added} new and {updated} existing spools.";
                if (result.Errors.Count > 0) message += $" {result.Errors.Count} rows skipped, see logs.";

                await viewModel.ShowSuccessMessage(message);

                // The import can be started from the spool list too, where the scan page banner isn't visible.
                toasts.Show(message, ToastKind.Success, true);
            }
            catch (Exception ex)
            {
                await viewModel.ShowErrorMessage("Import failed. See logs.");
                toasts.Show("Import failed. See logs.", ToastKind.Error, true);
                logger.LogError(ex, "Error in ImportCsvAsync");
            }
        }

        #endregion

        #region Sending stored spools

        private CancellationTokenSource? sendCancellation;

        /// <summary>Raised when sending stored spools starts, progresses or ends — for the spool list, which is rebuilt on every navigation.</summary>
        public event Action? SendStateChanged;

        /// <summary>The server stored spools can be sent to: the selected backend, unless that is "No backend".</summary>
        public ISpoolUploadTarget? SendTarget => ActiveManager as ISpoolUploadTarget;

        /// <summary>How far the current send is; null while nothing is being sent. <c>Done</c> 0 without a current spool is still connecting.</summary>
        public SpoolUploadProgress? SendProgress { get; private set; }

        public bool IsSendingSpools => SendProgress != null;

        /// <summary>The outcome of the last send, shown on the spool list until the next one starts or it is dismissed.</summary>
        public SpoolUploadSummary? LastSendSummary { get; private set; }

        /// <summary>
        /// Send stored spools to the selected server backend (see <see cref="NoBackendManager.SendSpoolsAsync"/>). Applies the current
        /// settings and connects the backend first — the spool list can be opened straight from settings, before the scan page has.
        /// Sent spools stay on the device, marked as sent.
        /// </summary>
        public async Task SendStoredSpoolsAsync(IReadOnlyCollection<string> keys)
        {
            if (IsSendingSpools || keys.Count == 0) return;

            try
            {
                sendCancellation = new CancellationTokenSource();
                LastSendSummary = null;
                SetSendProgress(new SpoolUploadProgress(0, keys.Count, null));

                // A spool open in the scan page's edit card would be saved to the last spool sent instead.
                CloseSpool();

                await ApplySettingsAsync();
                await InitializeBackendsAsync();

                var active = ActiveManager;

                if (active is not ISpoolUploadTarget target)
                {
                    toasts.Show("Select Spoolman or Bambuddy in Settings to send the stored spools there.", ToastKind.Error, true);
                    return;
                }

                if (active.Status != ManagerStatusType.Ready)
                {
                    toasts.Show($"Can't reach {active.Backend.DisplayName()}. Check its url in Settings and try again.", ToastKind.Error, true);
                    return;
                }

                var summary = await NoBackend.SendSpoolsAsync(target, keys, new InlineProgress(SetSendProgress), sendCancellation.Token);

                LastSendSummary = summary;

                // A single failure (e.g. the one spool sent from its own page) says why; more are listed on the spool list.
                var failures = summary.Failures.ToList();
                toasts.Show(failures.Count == 1 ? $"{summary.Text} {failures[0].Message}" : summary.Text, failures.Count > 0 ? ToastKind.Error : ToastKind.Success, true);
            }
            catch (Exception ex)
            {
                toasts.Show("Sending the stored spools failed. See logs.", ToastKind.Error, true);
                logger.LogError(ex, "Error in SendStoredSpoolsAsync");
            }
            finally
            {
                sendCancellation?.Dispose();
                sendCancellation = null;
                SetSendProgress(null);
            }
        }

        /// <summary>Stop sending after the spool being sent now; the ones already sent stay marked.</summary>
        public void CancelSendingSpools() => sendCancellation?.Cancel();

        public void DismissSendSummary()
        {
            LastSendSummary = null;
            SendStateChanged?.Invoke();
        }

        private void SetSendProgress(SpoolUploadProgress? progress)
        {
            SendProgress = progress;
            SendStateChanged?.Invoke();
        }

        /// <summary>
        /// Reports on the sending thread. <see cref="Progress{T}"/> would post each report instead, and a late one could arrive after
        /// the send finished and make it look running again.
        /// </summary>
        private sealed class InlineProgress(Action<SpoolUploadProgress> report) : IProgress<SpoolUploadProgress>
        {
            public void Report(SpoolUploadProgress value) => report(value);
        }

        #endregion

        #region Write OpenSpool tag

        // The spool the next tag held to the phone is written as; null while not writing.
        private ISpoolTag? pendingTagWrite;

        /// <summary>The spool tag read last (any format), which the write page can copy onto a new tag.</summary>
        public ISpoolTag? LastReadTag { get; private set; }

        /// <summary>Raised when writing starts, completes, fails or is cancelled.</summary>
        public event Action? TagWriteChanged;

        /// <summary>True while NFC waits for a tag to write <see cref="StartOpenSpoolWrite"/>'s spool to.</summary>
        public bool IsTagWritePending => pendingTagWrite != null;

        /// <summary>
        /// Whether this device can write NFC tags at all. Android only for now: the iOS write session is untested with
        /// this flow.
        /// </summary>
        public static bool CanWriteTags => DeviceInfo.Platform == DevicePlatform.Android && CrossNfc.IsSupported && CrossNfc.Current.IsAvailable && CrossNfc.Current.IsWritingTagSupported;

        /// <summary>
        /// Write <paramref name="spool"/> as an OpenSpool record to the next tag held to the phone. Reading spools is
        /// paused until then: while publishing, the NFC layer hands tags to <see cref="Current_OnTagDiscovered"/>.
        /// </summary>
        public void StartOpenSpoolWrite(ISpoolTag spool)
        {
            if (!CanWriteTags)
            {
                toasts.Show("This device can't write NFC tags.", ToastKind.Error, true);
                return;
            }

            pendingTagWrite = spool;

            CrossNfc.Current.OnTagDiscovered -= Current_OnTagDiscovered;
            CrossNfc.Current.OnTagDiscovered += Current_OnTagDiscovered;
            CrossNfc.Current.StartPublishing();

            TagWriteChanged?.Invoke();
        }

        /// <summary>Stop waiting for a tag to write and go back to reading spools.</summary>
        public void CancelTagWrite() => EndTagWrite();

        private void EndTagWrite()
        {
            pendingTagWrite = null;

            if (CrossNfc.IsSupported)
            {
                CrossNfc.Current.OnTagDiscovered -= Current_OnTagDiscovered;
                CrossNfc.Current.StopPublishing();
            }

            TagWriteChanged?.Invoke();
        }

        /// <summary>A tag held to the phone while writing: put the pending spool on it as an OpenSpool NDEF record.</summary>
        private async void Current_OnTagDiscovered(ITagInfo tagInfo, bool format)
        {
            try
            {
                var spool = pendingTagWrite;
                if (spool == null) return;

                // A Bambu Lab (MIFARE Classic) tag has no NDEF memory to write to.
                if (!tagInfo.IsSupported)
                {
                    toasts.Show("This tag can't hold an OpenSpool record. Use a blank NTAG215 or NTAG216.", ToastKind.Error, true);
                    return;
                }

                // Without the raw pages a TigerTag (binary, not NDEF) can't be ruled out, so never write blind.
                if (tagInfo is not TagInfo { UserMemory: not null })
                {
                    toasts.Show("The tag couldn't be read completely, it was not written. Hold it to the phone again.", ToastKind.Error, true);
                    return;
                }

                // Only a blank tag or an OpenSpool one is overwritten; a TigerTag or other data is kept.
                if (!SpoolTagReader.CanOverwrite(tagInfo))
                {
                    toasts.Show("This tag already holds other data (e.g. a TigerTag), it was not overwritten. Use a blank NTAG215 or NTAG216.", ToastKind.Error, true);
                    return;
                }

                if (!tagInfo.IsWritable)
                {
                    toasts.Show("This tag is read-only.", ToastKind.Error, true);
                    return;
                }

                tagInfo.Records = [OpenSpool.CreateRecord(spool)];

                // Throws (and stops publishing) when the write fails, e.g. the tag was moved away or is too small.
                CrossNfc.Current.PublishMessage(tagInfo);

                EndTagWrite();

                var name = string.Join(" ", new[] { spool.Brand, spool.DetailedFilamentType }.Where(x => !string.IsNullOrWhiteSpace(x)));

                await viewModel.AddLog(LogLevel.Success, $"OpenSpool tag {NfcUtils.ByteArrayToHexString(tagInfo.Identifier)} written: {OpenSpool.CreateJson(spool)}");
                toasts.Show($"OpenSpool tag written: {name}", ToastKind.Success, true);
            }
            catch (Exception e)
            {
                EndTagWrite();

                logger.LogWarning(e, "Error writing OpenSpool tag");
                toasts.Show($"Writing the tag failed: {e.Message}", ToastKind.Error, true);
            }
        }

        #endregion
    }
}
