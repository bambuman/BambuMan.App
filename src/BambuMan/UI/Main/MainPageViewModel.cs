using BambuMan.Shared;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.UI.Main
{
    public partial class MainPageViewModel(LogService logService, InventoryService inventoryService) : ObservableObject, IQueryAttributable
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

        [ObservableProperty] private decimal? spoolWeight;
        [ObservableProperty] private decimal? spoolEmptyWeight = 250;
        [ObservableProperty] private decimal? spoolInitialWeight;
        [ObservableProperty] private decimal? spoolPrice;
        [ObservableProperty] private DateTime? spoolBuyDate;
        [ObservableProperty] private string? spoolLotNr;
        [ObservableProperty] private string? spoolLocation;

        [ObservableProperty] private bool settingsOk;
        [ObservableProperty] private bool spoolmanOk;

        [ObservableProperty] private bool spoolmanConnecting = true;

        [ObservableProperty] private string backendLabel = "SPOOLMAN";

        // Edit-panel field visibility, driven by the active backend's BaseManager.EditFields.
        [ObservableProperty] private bool showBuyDate = true;
        [ObservableProperty] private bool showLotNr = true;

        [ObservableProperty] private string nfcText = "NFC ENABLED";

        [ObservableProperty] private string? errorMessage;
        [ObservableProperty] private string? successMessage;
        [ObservableProperty] private string? infoMessage;

        [ObservableProperty] private bool newVersionAvailable;
        [ObservableProperty] private string? newVersionText = "New version available";

        [ObservableProperty] private IEnumerable<string> existingLocations = [];

        [ObservableProperty] private bool showLogsOnMainPage;
        [ObservableProperty] private bool showKeyboardOnSpoolRead;

        [ObservableProperty] private bool fullTagScanAndUpload;

        [ObservableProperty] private bool overrideLocationOnRead;

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {

        }

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

        public void ShowSpool(SpoolFound found)
        {
            SpoolWeight = found.Weight;
            SpoolEmptyWeight = found.EmptyWeight;
            SpoolPrice = found.Price;
            SpoolBuyDate = found.BuyDate;
            SpoolLotNr = found.LotNr;
            SpoolLocation = found.Location;
            ShowSpoolEdit = true;
        }

        public async Task Validate(BaseManager manager)
        {
            await ClearMessages();

            if (string.IsNullOrEmpty(manager.ApiUrl))
            {
                await ShowErrorMessage("Inventory server url is missing, please fill in settings page!");
                return;
            }

            SettingsOk = true;

            SpoolmanOk = manager.Status == ManagerStatusType.Ready;

            if (!SpoolmanConnecting && !manager.IsHealth)
            {
                await ShowErrorMessage("Inventory api is not healthy");
                return;
            }

            if (!SpoolmanConnecting && !NfcIsEnabled)
                await ShowErrorMessage("NFC is not enabled. Check if you're phone supports nfc.");
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