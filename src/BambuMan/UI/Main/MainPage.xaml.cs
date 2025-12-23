using BambuMan.Interfaces;
using BambuMan.Shared;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Nfc;
using BambuMan.UI.Settings;
using CommunityToolkit.Maui.Core.Platform;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpoolMan.Api.Model;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.UI.Main
{
    public partial class MainPage
    {
        private readonly MainPageViewModel viewModel;

        private readonly SpoolmanManager spoolmanManager;
        private readonly ILogger<MainPage> logger;
        private readonly IToneGenerator? toneGenerator;
        private readonly IInvokeIndent invokeIndent;
        private Spool? currentSpool;
        private BambuFillamentInfo? currentBambuFillamentInfo;

        public MainPage(MainPageViewModel viewModel, SpoolmanManager spoolmanManager, ILogger<MainPage> logger, IToneGenerator toneGenerator, IInvokeIndent invokeIndent)
        {
            InitializeComponent();

            this.spoolmanManager = spoolmanManager;

            this.logger = logger;
            this.toneGenerator = toneGenerator;
            this.invokeIndent = invokeIndent;
            this.viewModel = viewModel;
            BindingContext = viewModel;

            viewModel.ShowLogsOnMainPage = Preferences.Default.Get(SettingsPage.ShowLogsOnMainPage, true);
            viewModel.ShowKeyboardOnSpoolRead = Preferences.Default.Get(SettingsPage.ShowKeyboardOnSpoolRead, true);
            viewModel.FullTagScanAndUpload = Preferences.Default.Get(SettingsPage.FullTagScanAndUpload, false);

            spoolmanManager.AppVersion = BuildVersionModel.CurrentBuildVersion;
            spoolmanManager.ShowLogs = true;
            spoolmanManager.ApiUrl = Preferences.Default.Get(SettingsPage.KeySpoolmanUrl, string.Empty);
            spoolmanManager.UnknownFilamentEnabled = Preferences.Default.Get(SettingsPage.UnknownFilamentEnabled, true);

            spoolmanManager.OnStatusChanged += SpoolmanManagerOnStatusChanged;
            spoolmanManager.OnLogMessage += SpoolmanManagerOnLogMessage;
            spoolmanManager.OnShowMessage += SpoolmanManagerOnShowMessage;
            spoolmanManager.OnSpoolFound += SpoolmanManagerOnSpoolFound;
            spoolmanManager.OnPlayErrorTone += SpoolmanManagerOnPlayErrorTone;
            spoolmanManager.OnLocationsLoaded += SpoolmanManagerOnLocationsLoaded;
        }

        private async void SpoolmanManagerOnPlayErrorTone()
        {
            try
            {
                if (toneGenerator != null) await toneGenerator.PlayAlarmTone();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in SpoolmanManagerOnPlayErrorTone");
            }
        }

        private async void SpoolmanManagerOnSpoolFound(Spool spool, BambuFillamentInfo info)
        {
            try
            {
                viewModel.InventorySpool(spool, info);

                currentSpool = spool;
                currentBambuFillamentInfo = info;

                viewModel.ShowSpool(spool);

                if (viewModel.ShowKeyboardOnSpoolRead)
                {
                    TfSpoolWeight.Focus();
                    TfSpoolWeight.SelectAllText();

                    await TfSpoolWeight.EntryView.ShowKeyboardAsync(CancellationToken.None);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in SpoolmanManagerOnSpoolFound");
            }
        }

        private async void SpoolmanManagerOnShowMessage(bool isError, string message)
        {
            try
            {
                await viewModel.ClearMessages();

                if (isError) await viewModel.ShowErrorMessage(message);
                else await viewModel.ShowSuccessMessage(message);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in SpoolmanManagerOnLogMessage");
            }
        }

        private async void SpoolmanManagerOnLogMessage(LogLevel level, string message)
        {
            try
            {
                await viewModel.AddLog(level, message);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in SpoolmanManagerOnLogMessage");
            }
        }

        private async void SpoolmanManagerOnStatusChanged()
        {
            try
            {
                if (viewModel.SpoolmanConnecting && spoolmanManager.Status == SpoolmanManagerStatusType.Ready) await viewModel.ShowInfoMessage("Ready to read spools.");

                if (spoolmanManager.Status >= SpoolmanManagerStatusType.Ready) viewModel.SpoolmanConnecting = false;

                await viewModel.Validate(spoolmanManager);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in SpoolmanManagerOnStatusChanged");
            }
        }


        private void SpoolmanManagerOnLocationsLoaded()
        {
            viewModel.ExistingLocations = spoolmanManager?.ExistingLocations ?? [];
        }

        protected override async void OnAppearing()
        {
            try
            {
                base.OnAppearing();

                viewModel.ShowSpoolEdit = false;
                currentSpool = null;
                currentBambuFillamentInfo = null;

                viewModel.ShowLogsOnMainPage = Preferences.Default.Get(SettingsPage.ShowLogsOnMainPage, true);
                viewModel.ShowKeyboardOnSpoolRead = Preferences.Default.Get(SettingsPage.ShowKeyboardOnSpoolRead, true);

                spoolmanManager.ApiUrl = Preferences.Default.Get(SettingsPage.KeySpoolmanUrl, string.Empty);
                spoolmanManager.UnknownFilamentEnabled = Preferences.Default.Get(SettingsPage.UnknownFilamentEnabled, true);

                viewModel.SpoolmanOk = false;
                viewModel.SpoolmanConnecting = true;

                //viewModel.Logs.Clear();
                await viewModel.Validate(spoolmanManager);

                await spoolmanManager.Init();

                // In order to support Mifare Classic 1K tags (read/write), you must set legacy mode to true.
                CrossNfc.Legacy = false;

                if (CrossNfc.IsSupported)
                {
                    viewModel.NfcIsEnabled = CrossNfc.Current.IsEnabled;
                    viewModel.NfcText = CrossNfc.Current.IsEnabled ? "NFC ENABLED" : "NFC DISABLED";

                    await viewModel.Validate(spoolmanManager);

                    if (DeviceInfo.Platform == DevicePlatform.iOS) viewModel.IsDeviceOs = true;

                    await AutoStartAsync().ConfigureAwait(false);
                }

                await CheckVersion().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in OnAppearing");
            }
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

                await viewModel.Validate(spoolmanManager);
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
                if (tagInfo == null)
                {
                    await ShowAlert("No tag found");
                    return;
                }

                // Customized serial number
                var identifier = tagInfo.Identifier;
                var serialNumber = NfcUtils.ByteArrayToHexString(identifier, ":");
                var title = !string.IsNullOrWhiteSpace(serialNumber) ? $"Tag [{serialNumber}]" : "Tag Info";

                if (tagInfo is BambuFillamentInfo bambuFillamentInfo)
                {
#if DEBUG
                    await viewModel.AddLog(LogLevel.Information, $"Nfc read time: {bambuFillamentInfo.ReadTime:0.###}ms");
#endif

                    var json = JsonConvert.SerializeObject(bambuFillamentInfo, Formatting.Indented);
                    await viewModel.AddLog(LogLevel.Information, json);

                    var buyDate = DateTime.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? (DateTime?)resultDate : null;
                    var defaultPrice = decimal.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? (decimal?)result : null;
                    var defaultLotNr = Preferences.Default.Get(SettingsPage.KeyDefaultLotNr, string.Empty);
                    var defaultLocation = Preferences.Default.Get(SettingsPage.KeyDefaultLocation, string.Empty);

                    await viewModel.ClearMessages();
                    await spoolmanManager.InventorySpool(bambuFillamentInfo, buyDate, defaultPrice, defaultLotNr, defaultLocation);
                    return;
                }

                if (!tagInfo.IsSupported)
                {
                    await viewModel.ShowErrorMessage("Error reading tag, please try again!");
                    if (toneGenerator != null) await toneGenerator.PlayAlarmTone();
                }
                else if (tagInfo.IsEmpty)
                {
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
                var httpClient = new HttpClient();
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
                    var bambuFillamentInfo = JsonConvert.DeserializeObject<BambuFillamentInfo>(json);

                    var jsonEnc = JsonConvert.SerializeObject(bambuFillamentInfo, Formatting.Indented);
                    await viewModel.AddLog(LogLevel.Information, jsonEnc);

                    var buyDate = DateTime.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? (DateTime?)resultDate : null;
                    var defaultPrice = decimal.TryParse(Preferences.Default.Get(SettingsPage.KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? (decimal?)result : null;
                    var defaultLotNr = Preferences.Default.Get(SettingsPage.KeyDefaultLotNr, string.Empty);
                    var defaultLocation = Preferences.Default.Get(SettingsPage.KeyDefaultLocation, string.Empty);

                    await viewModel.ClearMessages();
                    await spoolmanManager.InventorySpool(bambuFillamentInfo!, buyDate, defaultPrice, defaultLotNr, defaultLocation);

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
                currentSpool = null;
                currentBambuFillamentInfo = null;
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
            viewModel.HasInventoryItems = false;
            viewModel.Inventory.Clear();
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

        private async void SaveChanges_OnClicked(object? sender, EventArgs e)
        {
            try
            {
                await viewModel.ClearMessages();

                FormView.Submit();

                if (!FormView.IsValidated || currentSpool == null) return;

                await TfSpoolWeight.EntryView.HideKeyboardAsync(CancellationToken.None);

                await spoolmanManager.UpdateSpool(
                    currentSpool,
                    viewModel.SpoolBuyDate,
                    viewModel.SpoolPrice,
                    viewModel.SpoolLotNr,
                    viewModel.SpoolLocation,
                    viewModel.SpoolEmptyWeight.GetValueOrDefault(),
                    viewModel.SpoolInitialWeight.GetValueOrDefault(),
                    viewModel.SpoolWeight.GetValueOrDefault(),
                    currentBambuFillamentInfo?.TrayUid,
                    currentBambuFillamentInfo?.ProductionDateTime);

                viewModel.ShowSpoolEdit = false;
                currentSpool = null;
                currentBambuFillamentInfo = null;
            }
            catch (Exception ex)
            {
                await viewModel.AddLog(LogLevel.Error, "Error on save changes. " + e);
                logger.LogError(ex, "Error on spool save changes");
            }
        }
    }
}
