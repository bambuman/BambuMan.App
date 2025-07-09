using BambuMan.UI.Scan;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SpoolMan.Api.Api;
using SpoolMan.Api.Extensions;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace BambuMan.UI.Settings;

public partial class SettingsPage
{
    public const string KeySpoolmanUrl = "spoolman_url";
    public const string KeyDefaultBuyDate = "default_buy_date";
    public const string KeyDefaultPrice = "default_price";
    public const string KeyDefaultLotNr = "default_lot_nr";
    public const string KeyDefaultLocation = "default_location";
    public const string UnknownFilamentEnabled = "unknown_filament_enabled";
    public const string ShowLogsOnMainPage = "show_logs_on_main_page";
    public const string ShowKeyboardOnSpoolRead = "show_keyboard_on_spool_read";

    private readonly SettingsPageViewModel viewModel;
    private readonly ILogger<SettingsPage> logger;

    private IHost? apiHost;

    public SettingsPage(SettingsPageViewModel viewModel, ILogger<SettingsPage> logger)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        this.logger = logger;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        viewModel.SpoolmanUrl = Preferences.Default.Get(KeySpoolmanUrl, string.Empty);
        viewModel.BuyDate = DateTime.TryParse(Preferences.Default.Get(KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? resultDate : null;
        viewModel.DefaultPrice = decimal.TryParse(Preferences.Default.Get(KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? result : null;
        viewModel.DefaultLotNr = Preferences.Default.Get(KeyDefaultLotNr, string.Empty);
        viewModel.DefaultLocation = Preferences.Default.Get(KeyDefaultLocation, string.Empty);
        viewModel.UnknownFilamentEnabled = Preferences.Default.Get(UnknownFilamentEnabled, true);
        viewModel.ShowLogsOnMainPage = Preferences.Default.Get(ShowLogsOnMainPage, true);
        viewModel.ShowKeyboardOnSpoolRead = Preferences.Default.Get(ShowKeyboardOnSpoolRead, true);

        try
        {
            if (string.IsNullOrEmpty(viewModel.SpoolmanUrl))
            {
                return;
            }

            var apiUrl = viewModel.SpoolmanUrl.EndsWith("/") ? viewModel.SpoolmanUrl.Substring(0, viewModel.SpoolmanUrl.Length - 1) : viewModel.SpoolmanUrl;
            apiUrl = apiUrl.Contains("api/v1") ? apiUrl : $"{apiUrl}/api/v1";

            apiHost = Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
                {
                    services.AddApi(options =>
                    {
                        options.AddApiHttpClients(client =>
                        {
                            client.BaseAddress = new Uri(apiUrl);
                            client.Timeout = TimeSpan.FromSeconds(2);

                            if (!string.IsNullOrEmpty(client.BaseAddress.UserInfo))
                            {
                                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client.BaseAddress.UserInfo)));
                            }
                        });
                    });
                })
                .Build();
            
            var settingApi = apiHost.Services.GetRequiredService<ISettingApi>();

            var locationsRequest = settingApi.GetSettingSettingKeyGetOrDefaultAsync("locations").Result;

            if (locationsRequest != null && locationsRequest.TryOk(out var locations))
            {
                viewModel.ExistingLocations = JsonConvert.DeserializeObject<string[]>(locations.Value) ?? [];
                viewModel.LocationsFetched = true;
            }
        }
        catch (Exception)
        {
            //ignore
        }
    }

    private async void ImageButton_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(ScanPage));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error navigating to qrcode scanner");
        }
    }

    protected override void OnNavigatingFrom(NavigatingFromEventArgs args)
    {
        if (TfSpoolmanUrl.IsValid) Preferences.Default.Set(KeySpoolmanUrl, viewModel.SpoolmanUrl);

        if (TfBuyDate.IsValid)
        {
            if (TfBuyDate.Date == null || viewModel.BuyDate == null) Preferences.Default.Remove(KeyDefaultBuyDate);
            else Preferences.Default.Set(KeyDefaultBuyDate, $"{viewModel.BuyDate:yyyy-MM-dd}");
        }

        if (TfPrice.IsValid)
        {
            if (TfPrice.Text == string.Empty || viewModel.DefaultPrice == null) Preferences.Default.Remove(KeyDefaultPrice);
            else Preferences.Default.Set(KeyDefaultPrice, $"{viewModel.DefaultPrice:0.00}");
        }

        if (TfLotNr.IsValid) Preferences.Default.Set(KeyDefaultLotNr, viewModel.DefaultLotNr);

        if (TfLocation.IsValid) Preferences.Default.Set(KeyDefaultLocation, viewModel.DefaultLocation);

        if (TfUnknownFilamentEnabled.IsValid) Preferences.Default.Set(UnknownFilamentEnabled, viewModel.UnknownFilamentEnabled);

        if (TfShowLogsOnMainPage.IsValid) Preferences.Default.Set(ShowLogsOnMainPage, viewModel.ShowLogsOnMainPage);

        if (TfShowKeyboardOnSpoolRead.IsValid) Preferences.Default.Set(ShowKeyboardOnSpoolRead, viewModel.ShowKeyboardOnSpoolRead);

        base.OnNavigatingFrom(args);
    }
}