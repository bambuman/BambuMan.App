using BambuMan.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using HorusStudio.Maui.MaterialDesignControls;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace BambuMan.UI.Settings
{
    public partial class SettingsPageViewModel(ILogger<SettingsPageViewModel> logger) : ObservableObject, IQueryAttributable
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSpoolman))]
        [NotifyPropertyChangedFor(nameof(IsBambuddy))]
        [NotifyPropertyChangedFor(nameof(ServerUrl))]
        private InventoryBackend inventoryBackend = InventoryBackend.Spoolman;

        [ObservableProperty] private MaterialSegmentedButtonItem? selectedBackendItem;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ServerUrl))]
        private string? spoolmanUrl;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ServerUrl))]
        private string? bambuddyUrl;

        [ObservableProperty] private string? bambuddyApiKey;

        /// <summary>Single URL field shown in the UI; proxies to the active backend's stored URL (reloads on backend switch).</summary>
        public string? ServerUrl
        {
            get => InventoryBackend == InventoryBackend.Bambuddy ? BambuddyUrl : SpoolmanUrl;
            set
            {
                if (InventoryBackend == InventoryBackend.Bambuddy) BambuddyUrl = value;
                else SpoolmanUrl = value;
            }
        }
        [ObservableProperty] private decimal? defaultPrice;
        [ObservableProperty] private string? defaultLotNr;
        [ObservableProperty] private string? defaultLocation;
        [ObservableProperty] private DateTime? buyDate;
        [ObservableProperty] private bool unknownFilamentEnabled;
        [ObservableProperty] private bool showLogsOnMainPage;
        [ObservableProperty] private bool showKeyboardOnSpoolRead;
        [ObservableProperty] private bool fullTagScanAndUpload;
        [ObservableProperty] private bool overrideLocationOnRead;
        [ObservableProperty] private IEnumerable<string> existingLocations = [];

        [ObservableProperty] private bool locationsFetched;

        public ObservableCollection<MaterialSegmentedButtonItem> BackendItems { get; } =
            new(Enum.GetValues<InventoryBackend>().Select(b => new MaterialSegmentedButtonItem(b.ToString())));

        public bool IsSpoolman => InventoryBackend == InventoryBackend.Spoolman;
        public bool IsBambuddy => InventoryBackend == InventoryBackend.Bambuddy;

        partial void OnSelectedBackendItemChanged(MaterialSegmentedButtonItem? value)
        {
            if (value?.Text != null && Enum.TryParse<InventoryBackend>(value.Text, out var backend)) InventoryBackend = backend;
        }

        /// <summary>Sync the segmented selection to the current <see cref="InventoryBackend"/> (call after loading prefs).</summary>
        public void SyncBackendSelection() =>
            SelectedBackendItem = BackendItems.FirstOrDefault(i => i.Text == InventoryBackend.ToString());

        public async void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            try
            {
                // Runs after OnAppearing (which loads from prefs), so a scanned value wins.
                await Task.Delay(100);

                if (!query.TryGetValue("scan_value", out var raw) || raw is not string value || string.IsNullOrEmpty(value)) return;

                var target = query.TryGetValue("scan_target", out var t) ? t as string : null;
                switch (target)
                {
                    case "bambuddy_key": BambuddyApiKey = value; break;
                    default: ServerUrl = value; break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ApplyQueryAttributes");
            }
        }
    }
}
