using BambuMan.Shared;
using BambuMan.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace BambuMan.UI.Settings
{
    public partial class SettingsPageViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSpoolman))]
        [NotifyPropertyChangedFor(nameof(IsBambuddy))]
        [NotifyPropertyChangedFor(nameof(IsNoBackend))]
        [NotifyPropertyChangedFor(nameof(ShowServerUrl))]
        [NotifyPropertyChangedFor(nameof(ShowInventoryOptions))]
        [NotifyPropertyChangedFor(nameof(ServerUrl))]
        private InventoryBackend inventoryBackend = InventoryBackend.Bambuddy;

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
        [ObservableProperty] private bool storeSpoolsLocally;
        [ObservableProperty] private bool overrideLocationOnRead;
        [ObservableProperty] private IEnumerable<string> existingLocations = [];

        [ObservableProperty] private bool locationsFetched;

        [ObservableProperty] private string? serverUrlError;
        [ObservableProperty] private string? bambuddyApiKeyError;
        [ObservableProperty] private string? defaultPriceError;

        public bool IsSpoolman => InventoryBackend == InventoryBackend.Spoolman;
        public bool IsBambuddy => InventoryBackend == InventoryBackend.Bambuddy;
        public bool IsNoBackend => InventoryBackend == InventoryBackend.NoBackend;

        /// <summary>There is no server to point at without a backend, so the url field is hidden entirely.</summary>
        public bool ShowServerUrl => !IsNoBackend;

        /// <summary>
        /// Toggles that only mean anything when a spool is being written to a server — the unknown-filament import,
        /// the edit-panel conveniences and the server's location list. Meaningless with no backend, where the tag is
        /// only displayed. Default price and location still apply there: they go into the csv export.
        /// </summary>
        public bool ShowInventoryOptions => !IsNoBackend;

        /// <summary>Re-checks the fields that have rules, setting each one's message. True when the url is fine to store.</summary>
        public bool ValidateServerUrl()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl)) ServerUrlError = "Server url is required.";
            else if (!Regex.IsMatch(ServerUrl, Constants.UrlValidation)) ServerUrlError = "Invalid url, must start with http:// or https://";
            else ServerUrlError = null;

            return ServerUrlError == null;
        }

        public bool ValidateBambuddyApiKey()
        {
            BambuddyApiKeyError = string.IsNullOrWhiteSpace(BambuddyApiKey) ? "API key is required." : null;

            return BambuddyApiKeyError == null;
        }

        public bool ValidateDefaultPrice()
        {
            DefaultPriceError = DefaultPrice < 0 ? "Price can't be negative." : null;

            return DefaultPriceError == null;
        }

        /// <summary>Fills in what the QR scanner read for <paramref name="target"/> (<c>server_url</c> or <c>bambuddy_key</c>).</summary>
        public void ApplyScanResult(string target, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            // A combined Bambuddy config QR carries both fields — switch to Bambuddy first so ServerUrl routes
            // to the Bambuddy URL, then fill both.
            if (BambuddyConfigUri.TryParse(value, out var configUrl, out var configKey))
            {
                InventoryBackend = InventoryBackend.Bambuddy;
                if (!string.IsNullOrEmpty(configUrl)) ServerUrl = configUrl;
                if (!string.IsNullOrEmpty(configKey)) BambuddyApiKey = configKey;
                return;
            }

            switch (target)
            {
                case "bambuddy_key": BambuddyApiKey = value; break;
                default: ServerUrl = value; break;
            }
        }
    }
}
