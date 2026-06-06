using BambuMan.Interfaces;
using BambuMan.Shared;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
using BambuMan.UI.Settings;
using CommunityToolkit.Maui.Core.Platform;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.UI.Main
{
    public partial class MainPage
    {
        private readonly MainPageViewModel viewModel;

        private readonly IInventoryBackendResolver backends;
        private readonly ILogger<MainPage> logger;
        private readonly IToneGenerator? toneGenerator;
        private readonly IInvokeIndent invokeIndent;
        private readonly TagApiService tagApiService;

        public MainPage(MainPageViewModel viewModel, IInventoryBackendResolver backends, ILogger<MainPage> logger, IToneGenerator toneGenerator, IInvokeIndent invokeIndent, TagApiService tagApiService)
        {
            InitializeComponent();

            this.backends = backends;

            this.logger = logger;
            this.toneGenerator = toneGenerator;
            this.invokeIndent = invokeIndent;
            this.tagApiService = tagApiService;
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

            this.viewModel = viewModel;
            BindingContext = viewModel;

            viewModel.ShowLogsOnMainPage = Preferences.Default.Get(SettingsPage.ShowLogsOnMainPage, true);
            viewModel.ShowKeyboardOnSpoolRead = Preferences.Default.Get(SettingsPage.ShowKeyboardOnSpoolRead, true);
            viewModel.FullTagScanAndUpload = Preferences.Default.Get(SettingsPage.FullTagScanAndUpload, false);

        }

        private BaseManager ActiveManager => backends.Resolve(SettingsPage.GetInventoryBackend());

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

        private async void ManagerOnSpoolFound(SpoolFound found, BambuFilamentInfo info)
        {
            try
            {
                viewModel.InventorySpool(found, info);

                viewModel.ShowSpool(found);

                if (viewModel.ShowKeyboardOnSpoolRead)
                {
                    TfSpoolWeight.Focus();
                    TfSpoolWeight.SelectAllText();

                    await TfSpoolWeight.EntryView.ShowKeyboardAsync(CancellationToken.None);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in ManagerOnSpoolFound");
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
                    var toast = CommunityToolkit.Maui.Alerts.Toast.Make(message, CommunityToolkit.Maui.Core.ToastDuration.Long);
                    await toast.Show();
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

        protected override async void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                await SetupEventSubscriptionsAsync();
                await InitializeBackendsAsync();
                await SetupNfcAsync();
#if !GOOGLE_PLAY
                await CheckVersion().ConfigureAwait(false);
#endif
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in OnAppearing");
            }
        }

        private async Task SetupEventSubscriptionsAsync()
        {
            try
            {
                foreach (var manager in backends.All)
                {
                    manager.OnStatusChanged += ManagerOnStatusChanged;
                    manager.OnLogMessage += ManagerOnLogMessage;
                    manager.OnShowMessage += ManagerOnShowMessage;
                    manager.OnPlayErrorTone += ManagerOnPlayErrorTone;
                    manager.OnSpoolFound += ManagerOnSpoolFound;
                    manager.OnLocationsLoaded += ManagerOnLocationsLoaded;
                }

                viewModel.PropertyChanged += ViewModel_PropertyChanged;

                viewModel.ShowSpoolEdit = false;

                viewModel.ShowLogsOnMainPage = Preferences.Default.Get(SettingsPage.ShowLogsOnMainPage, true);
                viewModel.ShowKeyboardOnSpoolRead = Preferences.Default.Get(SettingsPage.ShowKeyboardOnSpoolRead, true);

                var active = ActiveManager;
                var activeChanged = ConfigureBackends(active);

                viewModel.OverrideLocationOnRead = active.OverrideLocationOnRead;
                viewModel.ExistingLocations = active.ExistingLocations;
                viewModel.BackendLabel = active.Backend.ToString().ToUpperInvariant();
                viewModel.ShowBuyDate = active.EditFields.BuyDate;
                viewModel.ShowLotNr = active.EditFields.LotNr;

                // Only show connecting animation if the active backend's config changed or it's not yet initialized
                if (activeChanged || !active.IsInitialized)
                {
                    viewModel.SpoolmanOk = false;
                    viewModel.SpoolmanConnecting = true;
                }

                //viewModel.Logs.Clear();
                await viewModel.Validate(active);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in OnAppearing setup");
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
                }

                if (manager.Backend == active.Backend) activeChanged = changed;
            }

            return activeChanged;
        }

        private async Task InitializeBackendsAsync()
        {
            foreach (var manager in backends.All)
            {
                try
                {
                    await manager.Init();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error initializing {Backend}", manager.Backend);
                }
            }

            _ = ActiveManager.RefreshLocationsAsync();
        }

        private async Task SetupNfcAsync()
        {
            try
            {
                // In order to support Mifare Classic 1K tags (read/write), you must set legacy mode to true.
                CrossNfc.Legacy = false;

                if (CrossNfc.IsSupported)
                {
                    viewModel.NfcIsEnabled = CrossNfc.Current.IsEnabled;
                    viewModel.NfcText = CrossNfc.Current.IsEnabled ? "NFC ENABLED" : "NFC DISABLED";

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

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            foreach (var manager in backends.All)
            {
                manager.OnStatusChanged -= ManagerOnStatusChanged;
                manager.OnLogMessage -= ManagerOnLogMessage;
                manager.OnShowMessage -= ManagerOnShowMessage;
                manager.OnPlayErrorTone -= ManagerOnPlayErrorTone;
                manager.OnSpoolFound -= ManagerOnSpoolFound;
                manager.OnLocationsLoaded -= ManagerOnLocationsLoaded;
            }

            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        protected override bool OnBackButtonPressed()
        {
            Task.Run(StopListening);
            return base.OnBackButtonPressed();
        }

        #region Helpers

        /// <summary>
        /// Write a debug message in the debug console
        /// </summary>
        /// <param name="message">The message to be displayed</param>
        private void Debug(string message) => System.Diagnostics.Debug.WriteLine(message);

        /// <summary>
        /// Display an alert
        /// </summary>
        /// <param name="message">Message to be displayed</param>
        /// <param name="title">Alert title</param>
        /// <returns>The task to be performed</returns>
        private Task ShowAlert(string message, string? title = null) => DisplayAlertAsync(string.IsNullOrWhiteSpace(title) ? "NFC" : title, message, "OK");


        #endregion

        #region Nfc Logic

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
        /// Task to safely stop listening for NFC tags
        /// </summary>
        /// <returns>The task to be performed</returns>
        private async Task StopListening()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CrossNfc.Current.StopListening();
                    UnsubscribeEvents();
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

        private void Current_OnTagIntentReceived(object? sender, EventArgs e)
        {
            CloseButton_OnClicked(sender, e);
        }

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
                viewModel.NfcText = isEnabled ? "NFC ENABLED" : "NFC DISABLED";

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

                    var buyDate = DateTime.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? (DateTime?)resultDate : null;
                    var defaultPrice = decimal.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? (decimal?)result : null;
                    var defaultLotNr = Preferences.Default.Get(SettingsPage.KeyDefaultLotNr, string.Empty);
                    var defaultLocation = Preferences.Default.Get(SettingsPage.KeyDefaultLocation, string.Empty);

                    await viewModel.ClearMessages();

                    var active = ActiveManager;
                    SentrySdk.ConfigureScope(s => s.SetTag("inventory.backend", active.Backend.ToString()));
                    await active.InventorySpool(bambuFilamentInfo, buyDate, defaultPrice, defaultLotNr, defaultLocation);

                    if (viewModel.FullTagScanAndUpload)
                    {
                        var (_, rateLimited) = await tagApiService.UploadNfcTagAsync(bambuFilamentInfo);
                        if (rateLimited)
                        {
                            await viewModel.ShowErrorMessage("Daily upload limit reached (1000 tags/day). Try again tomorrow.");
                        }
                    }

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

        /// <summary>
        /// Event raised when user cancelled NFC session on iOS
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Current_OniOSReadingSessionCancelled(object? sender, EventArgs e) => Debug("iOS NFC Session has been cancelled");

        /// <summary>
        /// Returns the tag information from NDEF record
        /// </summary>
        /// <param name="record"><see cref="NfcNdefRecord"/></param>
        /// <returns>The tag information</returns>
        private string GetMessage(NfcNdefRecord record)
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
            }
            catch (Exception)
            {
                //ignore
            }
        }

        #endregion

        #region Test Stuff

        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        private async void Button_OnClicked(object? sender, EventArgs e)
        {
            try
            {
                //var json = "{\"SerialNumber\":\"C3DB40A2\",\"TagManufacturerData\":\"+ggEAARS8na7x5uQ\",\"MaterialVariantIdentifier\":\"A00-D0\",\"UniqueMaterialIdentifier\":\"FA00\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Basic\",\"Color\":\"8E9089FF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"0AfQB+gD6AOamRk/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"F1FACEE5124249F6AEB7DCEC0AAE0C4F\",\"SpoolWidth\":2875,\"ProductionDateTime\":\"2025-01-20T19:14:00\",\"ProductionDateTimeShort\":\"20250120\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A00-D0-1.75-1000\"}";
                //var json = "{\"SerialNumber\":\"83EC9A1C\",\"TagManufacturerData\":\"6QgEAAR8EZ2x4zmQ\",\"MaterialVariantIdentifier\":\"A17-R1\",\"UniqueMaterialIdentifier\":\"FA17\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Translucent\",\"Color\":\"F5B6CD80\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":240,\"MinTemperatureForHotend\":200,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"2DC9E553D1924FA89FBB893C9E921DBA\",\"SpoolWidth\":666,\"ProductionDateTime\":\"2024-12-20T09:40:00\",\"ProductionDateTimeShort\":\"20241220\",\"FilamentLength\":345,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A17-R1-1.75-1000\"}";
                //var json = "{\"SerialNumber\":\"5B1449F6\",\"TagManufacturerData\":\"8AgEAATrOVf5DLaQ\",\"MaterialVariantIdentifier\":\"A16-G0\",\"UniqueMaterialIdentifier\":\"FA16\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Wood\",\"Color\":\"918669FF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":60,\"DryingTime\":6,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"4663E9ADF9CC454380EB58CE627BFE72\",\"SpoolWidth\":1536,\"ProductionDateTime\":\"2025-03-11T00:38:00\",\"ProductionDateTimeShort\":\"25_03_11_00\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A16-G0-1.75-1000\"}";

                var jsons = new[]
                {
                    "{\"SerialNumber\":\"5B1449F6\",\"TagManufacturerData\":\"8AgEAATrOVf5DLaQ\",\"MaterialVariantIdentifier\":\"A16-G0\",\"UniqueMaterialIdentifier\":\"FA16\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Wood\",\"Color\":\"918669FF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":60,\"DryingTime\":6,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"4663E9ADF9CC454380EB58CE627BFE72\",\"SpoolWidth\":1536,\"ProductionDateTime\":\"2025-03-11T00:38:00\",\"ProductionDateTimeShort\":\"25_03_11_00\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A16-G0-1.75-1000\"}",
                    "{\"SerialNumber\":\"0509F50D\",\"TagManufacturerData\":\"9AgEAASm165pzJOQ\",\"MaterialVariantIdentifier\":\"A16-R0\",\"UniqueMaterialIdentifier\":\"FA16\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Wood\",\"Color\":\"3F231CFF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":60,\"DryingTime\":6,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"26E72842404F41F2A227FC7276299DFA\",\"SpoolWidth\":1536,\"ProductionDateTime\":\"2025-03-24T14:32:00\",\"ProductionDateTimeShort\":\"25_03_24_14\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A16-R0-1.75-1000\"}"
                };

                foreach (var json in jsons)
                {
                    var bambuFilamentInfo = JsonConvert.DeserializeObject<BambuFilamentInfo>(json);

                    var jsonEnc = JsonConvert.SerializeObject(bambuFilamentInfo, Formatting.Indented);
                    await viewModel.AddLog(LogLevel.Information, jsonEnc);

                    var buyDate = DateTime.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? (DateTime?)resultDate : null;
                    var defaultPrice = decimal.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? (decimal?)result : null;
                    var defaultLotNr = Preferences.Default.Get(SettingsPage.KeyDefaultLotNr, string.Empty);
                    var defaultLocation = Preferences.Default.Get(SettingsPage.KeyDefaultLocation, string.Empty);

                    await viewModel.ClearMessages();

                    await ActiveManager.InventorySpool(bambuFilamentInfo!, buyDate, defaultPrice, defaultLotNr, defaultLocation);

                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error on Test button click");
            }
        }

        private async void CloseButton_OnClicked(object? sender, EventArgs e)
        {
            try
            {
                await TfSpoolWeight.EntryView.HideKeyboardAsync(CancellationToken.None);

                viewModel.ShowSpoolEdit = false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in CloseButton_OnClicked");
            }
        }

        private void ClearLogs_OnClicked(object? sender, EventArgs e)
        {
            viewModel.Logs.Clear();
        }

        private void ClearInventory_OnClicked(object? sender, EventArgs e)
        {
            viewModel.ClearInventory();
        }

        private async void EmailLogs_OnClicked(object? sender, EventArgs e)
        {
            try
            {
                var logs = $"{string.Join("\r\n", viewModel.Logs.Select(x => x.Content))}\r\n\r\n";
                await invokeIndent.SendEmail("bambuman.app@gmail.com", "Bambu logs", logs);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in EmailLogs_OnClicked");
            }
        }

        #endregion

        private async void Settings_OnClicked(object? sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("//SettingsPage");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Settings_OnClicked");
            }
        }

        private async void SaveChanges_OnClicked(object? sender, EventArgs e)
        {
            try
            {
                await viewModel.ClearMessages();

                FormView.Submit();

                if (!FormView.IsValidated) return;

                await TfSpoolWeight.EntryView.HideKeyboardAsync(CancellationToken.None);

                var input = new SpoolEditInput(
                    viewModel.SpoolWeight,
                    viewModel.SpoolEmptyWeight,
                    viewModel.SpoolPrice,
                    viewModel.SpoolBuyDate,
                    viewModel.SpoolLotNr,
                    viewModel.SpoolLocation);

                await ActiveManager.UpdateCurrentSpoolAsync(input);

                viewModel.ShowSpoolEdit = false;
            }
            catch (Exception ex)
            {
                await viewModel.AddLog(LogLevel.Error, "Error on save changes. " + e);
                logger.LogError(ex, "Error on spool save changes");
            }
        }
    }
}
