using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Settings
{
    public partial class SettingsPageViewModel(ILogger<SettingsPageViewModel> logger) : ObservableObject, IQueryAttributable
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
        [ObservableProperty] private bool overrideLocationOnRead;
        [ObservableProperty] private IEnumerable<string> existingLocations = [];

        [ObservableProperty] private bool locationsFetched;

        public async void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            try
            {
                await Task.Delay(100);
                SpoolmanUrl = (query.TryGetValue("url", out var url) ? url as string : null) ??
                              Preferences.Default.Get("spoolman_url", string.Empty);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ApplyQueryAttributes");
            }
        }
    }
}
