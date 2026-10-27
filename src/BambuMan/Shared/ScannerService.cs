using BambuMan.Interfaces;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
using BambuMan.Shared.Resolvers;
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

        private bool versionChecked;

        public ScannerService(MainPageViewModel viewModel, IInventoryBackendResolver backends, ILogger<ScannerService> logger, IToneGenerator toneGenerator, IInvokeIndent invokeIndent, TagApiService tagApiService, IFilamentOverrideService filamentOverrides, ToastService toasts)
        {
            this.viewModel = viewModel;
            this.backends = backends;
            this.logger = logger;
            this.toneGenerator = toneGenerator;
            this.invokeIndent = invokeIndent;
            this.tagApiService = tagApiService;
            this.filamentOverrides = filamentOverrides;
            this.toasts = toasts;

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
                if (toneGenerator != null) await toneGenerator.PlayAlarmTone();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in ManagerOnPlayErrorTone");
            }
        }

        private void ManagerOnSpoolFound(SpoolFound found, BambuFilamentInfo info)
        {
            // On the main thread, so the inventory counter is updated before HasInventoryItems is read back from it.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    viewModel.InventorySpool(found, info);

                    // A backend that can't save edits shows the info card from OnSpoolInfoRead instead.
                    if (!ActiveManager.CanEditSpools) return;

                    viewModel.ShowSpool(found, info);

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
        /// be opened straight from settings before the scan page has re-applied it.
        /// </summary>
        public async Task PrepareLocalInventoryAsync()
        {
            var noBackend = NoBackend;
            var storeLocally = Preferences.Default.Get(SettingsPage.KeyStoreSpoolsLocally, false);

            noBackend.StoreSpoolsLocally = storeLocally;

            if (!noBackend.IsInitialized || noBackend.IsStoreLoaded != storeLocally) await noBackend.Init();
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

                if (!tagInfo.IsSupported)
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

        /// <summary>Inventory a read tag against the active backend with the configured defaults, then upload it if opted in.</summary>
        private async Task InventorySpoolAsync(BambuFilamentInfo info)
        {
            var buyDate = DateTime.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? (DateTime?)resultDate : null;
            var defaultPrice = decimal.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? (decimal?)result : null;
            string? defaultLotNr = Preferences.Default.Get(SettingsPage.KeyDefaultLotNr, string.Empty);
            string? defaultLocation = Preferences.Default.Get(SettingsPage.KeyDefaultLocation, string.Empty);

            await viewModel.ClearMessages();

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
            await active.InventorySpool(info, buyDate, defaultPrice, defaultLotNr, defaultLocation);

            if (viewModel.FullTagScanAndUpload)
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
    }
}
