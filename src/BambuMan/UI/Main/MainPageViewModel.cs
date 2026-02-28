using BambuMan.Shared;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using SpoolMan.Api.Model;
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
            Logs.Insert(0, new LogModel(level, text));

            return Task.CompletedTask;
        }

        public void ShowSpool(Spool spool)
        {
            SpoolWeight = spool.SpoolWeight.GetValueOrDefault() + spool.InitialWeight.GetValueOrDefault() - spool.UsedWeight;
            SpoolInitialWeight = spool.InitialWeight;
            SpoolEmptyWeight = spool.SpoolWeight;
            SpoolPrice = spool.Price;
            SpoolBuyDate = spool.Extra.TryGetValue(SpoolmanManager.ExtraBuyDate, out var buyDateOut) ? DateTime.Parse(buyDateOut.Replace("\"", "")) : null;
            SpoolLotNr = spool.LotNr;
            SpoolLocation = spool.Location;
            ShowSpoolEdit = true;
        }

        public async Task Validate(SpoolmanManager spoolmanManager)
        {
            await ClearMessages();

            if (string.IsNullOrEmpty(spoolmanManager.ApiUrl))
            {
                await ShowErrorMessage("Spoolman url is missing, please fill in settings page!");
                return;
            }

            SettingsOk = true;

            if (!SpoolmanConnecting && !spoolmanManager.IsHealth)
            {
                await ShowErrorMessage("Spoolman api is not healthy");
                return;
            }

            if (spoolmanManager.Status >= SpoolmanManagerStatusType.Ready) SpoolmanOk = true;

            if (!SpoolmanConnecting && !NfcIsEnabled)
                await ShowErrorMessage("NFC is not enabled. Check if you're phone supports nfc.");
        }

        public void InventorySpool(Spool spool, BambuFillamentInfo info)
        {
            inventoryService.InventorySpool(spool, info);
            HasInventoryItems = inventoryService.HasItems;
        }

        public void ClearInventory()
        {
            inventoryService.Clear();
            HasInventoryItems = false;
        }
    }
}