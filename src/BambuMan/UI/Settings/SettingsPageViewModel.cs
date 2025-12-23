using CommunityToolkit.Mvvm.ComponentModel;

namespace BambuMan.UI.Settings
{
    public partial class SettingsPageViewModel : ObservableObject, IQueryAttributable
    {
        [ObservableProperty] private string? spoolmanUrl;
        [ObservableProperty] private decimal? defaultPrice;
        [ObservableProperty] private string? defaultLotNr;
        [ObservableProperty] private string? defaultLocation;
        [ObservableProperty] private DateTime? buyDate;
        [ObservableProperty] private bool unknownFilamentEnabled;
        [ObservableProperty] private bool showLogsOnMainPage;
        [ObservableProperty] private bool showKeyboardOnSpoolRead;
        [ObservableProperty] private bool fullTagScanAndUpload;
        [ObservableProperty] private IEnumerable<string> existingLocations = [];

        [ObservableProperty] private bool locationsFetched;

        public async void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            await Task.Delay(500);
            SpoolmanUrl = query["url"] as string ?? Preferences.Default.Get("spoolman_url", string.Empty);
        }
    }
}
