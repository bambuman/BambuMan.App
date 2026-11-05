using BambuMan.Components.Common;
using BambuMan.Resources.Strings;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.UI.Main
{
    /// <summary>State of the scan page. A singleton, so it survives navigating to the other pages and back.</summary>
    public partial class MainPageViewModel(LogService logService, InventoryService inventoryService) : ObservableObject
    {

#if DEBUG
        [ObservableProperty] private bool isTest = true;
#else
        [ObservableProperty] private bool isTest = false;
#endif

        [ObservableProperty] private bool hasInventoryItems = inventoryService.HasItems;
        [ObservableProperty] private ObservableCollection<InventoryModel> inventory = inventoryService.Inventory;

        [ObservableProperty] private bool deviceIsListening;
        [ObservableProperty] private ObservableCollection<LogModel> logs = logService.Logs;
        [ObservableProperty] private bool nfcIsEnabled;
        [ObservableProperty] private bool eventsAlreadySubscribed;
        [ObservableProperty] private bool isDeviceOs;

        [ObservableProperty] private bool showSpoolEdit;

        // The read-only counterpart of the edit panel, used by backends with nothing to write to.
        [ObservableProperty] private bool showSpoolInfo;
        [ObservableProperty] private SpoolDisplayInfo? spoolInfo;

        // False when read spools can't be edited (no backend without the device store), which hides the controls
        // that only make sense when a spool is saved somewhere.
        [ObservableProperty] private bool showInventoryOptions = true;

        // The no-backend spool bar: export always, list + import only while spools are stored on the device.
        [ObservableProperty] private bool showLocalSpools;
        [ObservableProperty] private bool localSpoolsStored;
        [ObservableProperty] private string? localSpoolsText = AppResources.Main_ScannedSpools;
        [ObservableProperty] private int localSpoolsCount;

        // The spool behind the edit card, kept whole so the card can show tag fields (colour, material) directly.
        [ObservableProperty] private SpoolFound? spool;
        [ObservableProperty] private BambuFilamentInfo? spoolTag;

        [ObservableProperty] private SpoolEditModel spoolEdit = new();

        [ObservableProperty] private bool settingsOk;
        [ObservableProperty] private bool spoolmanOk;

        [ObservableProperty] private bool spoolmanConnecting = true;

        [ObservableProperty] private InventoryBackend backend = InventoryBackend.Spoolman;
        [ObservableProperty] private string backendLabel = "Spoolman";
        [ObservableProperty] private ManagerStatusType backendStatus = ManagerStatusType.Initializing;

        // Edit-panel field visibility, driven by the active backend's BaseManager.EditFields.
        [ObservableProperty] private SpoolEditFields editFields = new(BuyDate: true, LotNr: true);

        [ObservableProperty] private string nfcText = AppResources.Main_NfcEnabled;

        [ObservableProperty] private string? errorMessage;
        [ObservableProperty] private string? successMessage;
        [ObservableProperty] private string? infoMessage;

        [ObservableProperty] private bool newVersionAvailable;
        // The release tag, not the banner text: the text is formatted on every read, so it follows a language switch.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NewVersionText))]
        private string? newVersionTag;

        public string NewVersionText => NewVersionTag == null ? AppResources.Main_NewVersionAvailable : string.Format(AppResources.Main_NewVersionAvailableTag, NewVersionTag);

        [ObservableProperty] private IEnumerable<string> existingLocations = [];

        [ObservableProperty] private bool showLogsOnMainPage;
        [ObservableProperty] private bool showKeyboardOnSpoolRead;

        [ObservableProperty] private bool fullTagScanAndUpload;

        [ObservableProperty] private bool overrideLocationOnRead;

        public Task ClearMessages()
        {
            ErrorMessage = null;
            SuccessMessage = null;
            InfoMessage = null;

            return Task.CompletedTask;
        }

        public Task ShowErrorMessage(string message)
        {
            ErrorMessage = message;
            SuccessMessage = null;
            InfoMessage = null;

            return Task.CompletedTask;
        }

        public Task ShowSuccessMessage(string message)
        {
            ErrorMessage = null;
            SuccessMessage = message;
            InfoMessage = null;

            return Task.CompletedTask;
        }
        public Task ShowInfoMessage(string message)
        {
            ErrorMessage = null;
            SuccessMessage = null;
            InfoMessage = message;

            return Task.CompletedTask;
        }

        public Task AddLog(LogLevel level, string text)
        {
            return logService.AddLog(level, text);
        }

        public void ShowSpool(SpoolFound found, BambuFilamentInfo info)
        {
            Spool = found;
            SpoolTag = info;
            SpoolEdit = SpoolEditModel.From(found);
            ShowSpoolEdit = true;
        }

        public void ShowReadOnlySpool(SpoolDisplayInfo info)
        {
            SpoolInfo = info;
            ShowSpoolInfo = true;
        }

        public async Task Validate(BaseManager manager)
        {
            await ClearMessages();

            // A read-only backend has no url to check and no api to be unhealthy — without this the missing-url
            // banner below would sit on screen permanently.
            if (manager.IsReadOnly)
            {
                SettingsOk = true;
                SpoolmanOk = manager.Status == ManagerStatusType.Ready;

                if (!SpoolmanConnecting && !NfcIsEnabled)
                    await ShowErrorMessage(AppResources.Main_ErrorNfcNotEnabled);

                return;
            }

            if (string.IsNullOrEmpty(manager.ApiUrl))
            {
                await ShowErrorMessage(AppResources.Main_ErrorServerUrlMissing);
                return;
            }

            SettingsOk = true;

            SpoolmanOk = manager.Status == ManagerStatusType.Ready;

            if (!SpoolmanConnecting && !manager.IsHealth)
            {
                await ShowErrorMessage(AppResources.Main_ErrorApiNotHealthy);
                return;
            }

            if (!SpoolmanConnecting && !NfcIsEnabled)
                await ShowErrorMessage(AppResources.Main_ErrorNfcNotEnabled);
        }

        public void InventorySpool(SpoolFound found, BambuFilamentInfo info)
        {
            inventoryService.InventorySpool(found, info);
            HasInventoryItems = inventoryService.HasItems;
        }

        public void ClearInventory()
        {
            inventoryService.Clear();
            HasInventoryItems = false;
        }
    }
}