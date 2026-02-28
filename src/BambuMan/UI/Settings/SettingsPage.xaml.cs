using BambuMan.Shared;
using BambuMan.UI.Consent;
using BambuMan.UI.Scan;
using CommunityToolkit.Maui;
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
    public const string FullTagScanAndUpload = "full_tag_scan_and_upload";
    public const string TagUploadConsentShown = "tag_upload_consent_shown";

    private readonly SettingsPageViewModel viewModel;
    private readonly ILogger<SettingsPage> logger;
    private readonly IPopupService popupService;
    private readonly SpoolmanManager spoolmanManager;

    private IHost? apiHost;
    private string? apiHostUrl;

    public SettingsPage(SettingsPageViewModel viewModel, ILogger<SettingsPage> logger, IPopupService popupService, SpoolmanManager spoolmanManager)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        this.logger = logger;
        this.popupService = popupService;
        this.spoolmanManager = spoolmanManager;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
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
        viewModel.FullTagScanAndUpload = Preferences.Default.Get(FullTagScanAndUpload, false);
        viewModel.OverrideLocationOnRead = spoolmanManager.OverrideLocationOnRead;

        await ShowConsentPopupIfNeeded();

        try
        {
            if (!EnsureApiHost()) return;

            var settingApi = apiHost!.Services.GetRequiredService<ISettingApi>();

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

    private bool EnsureApiHost()
    {
        if (string.IsNullOrEmpty(viewModel.SpoolmanUrl)) return false;

        if (apiHost != null && apiHostUrl == viewModel.SpoolmanUrl) return true;

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
                    }, builder =>
                    {
                        builder
                            .AddRetryPolicy(3)
                            .AddCircuitBreakerPolicy(5, TimeSpan.FromSeconds(30));
                    });
                });
            })
            .Build();

        apiHostUrl = viewModel.SpoolmanUrl;
        return true;
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

    private async Task ShowConsentPopupIfNeeded()
    {
        var consentShown = Preferences.Default.Get(TagUploadConsentShown, false);
        if (consentShown) return;

        var popupResult = await popupService.ShowPopupAsync<TagUploadConsentPopup, bool>(Shell.Current, new PopupOptions
        {
            CanBeDismissedByTappingOutsideOfPopup = false
        });

        Preferences.Default.Set(TagUploadConsentShown, true);

        if (popupResult is { WasDismissedByTappingOutsideOfPopup: false })
        {
            Preferences.Default.Set(FullTagScanAndUpload, popupResult.Result);
            viewModel.FullTagScanAndUpload = popupResult.Result;
        }
    }

    private async void InfoButton_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            var popupResult = await popupService.ShowPopupAsync<TagUploadConsentPopup, bool>(Shell.Current, new PopupOptions
            {
                CanBeDismissedByTappingOutsideOfPopup = true
            });

            if (popupResult is { WasDismissedByTappingOutsideOfPopup: false })
            {
                Preferences.Default.Set(FullTagScanAndUpload, popupResult.Result);
                viewModel.FullTagScanAndUpload = popupResult.Result;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error showing consent popup");
        }
    }

    private async void RefreshLocations_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            if (!EnsureApiHost())
            {
                await CommunityToolkit.Maui.Alerts.Toast.Make("Set a Spoolman URL first").Show();
                return;
            }

            var settingApi = apiHost!.Services.GetRequiredService<ISettingApi>();
            var locationsRequest = await settingApi.GetSettingSettingKeyGetOrDefaultAsync("locations");

            if (locationsRequest != null && locationsRequest.TryOk(out var locations))
            {
                viewModel.ExistingLocations = JsonConvert.DeserializeObject<string[]>(locations.Value) ?? [];
                viewModel.LocationsFetched = true;
            }

            await CommunityToolkit.Maui.Alerts.Toast.Make("Locations refreshed").Show();
        }
        catch (Exception)
        {
            await CommunityToolkit.Maui.Alerts.Toast.Make("Failed to refresh locations").Show();
        }
    }

    private async void TestSpoolmanUrl_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            var url = viewModel.SpoolmanUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                await CommunityToolkit.Maui.Alerts.Toast.Make("Spoolman URL is empty").Show();
                return;
            }

            var apiUrl = url.EndsWith("/") ? url.Substring(0, url.Length - 1) : url;
            apiUrl = apiUrl.Contains("api/v1") ? apiUrl : $"{apiUrl}/api/v1";

            var testHost = Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
                {
                    services.AddApi(options =>
                    {
                        options.AddApiHttpClients(client =>
                        {
                            client.BaseAddress = new Uri(apiUrl);
                            client.Timeout = TimeSpan.FromSeconds(2);

                            if (!string.IsNullOrEmpty(client.BaseAddress.UserInfo))
                                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client.BaseAddress.UserInfo)));
                        });
                    });
                })
                .Build();

            var defaultApi = testHost.Services.GetRequiredService<IDefaultApi>();
            var healthResult = await defaultApi.HealthHealthGetAsync();

            if (healthResult.TryOk(out _))
                await CommunityToolkit.Maui.Alerts.Toast.Make("Connection successful", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
            else
                await CommunityToolkit.Maui.Alerts.Toast.Make($"Connection failed: {healthResult.RawContent}", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
        }
        catch (TaskCanceledException)
        {
            await CommunityToolkit.Maui.Alerts.Toast.Make("Connection timed out — check the URL and ensure Spoolman is running", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
        }
        catch (HttpRequestException)
        {
            await CommunityToolkit.Maui.Alerts.Toast.Make("Could not reach Spoolman — check the URL and network connection", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
        }
        catch (UriFormatException)
        {
            await CommunityToolkit.Maui.Alerts.Toast.Make("Invalid URL format", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
        }
        catch (Exception ex)
        {
            logger.LogDebug("Test Spoolman URL failed: {Message}", ex.Message);
            await CommunityToolkit.Maui.Alerts.Toast.Make($"Connection failed: {ex.Message}", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
        }
    }

    private async void BackToMain_OnClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage");
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

        if (TfFullTagScanAndUpload.IsValid) Preferences.Default.Set(FullTagScanAndUpload, viewModel.FullTagScanAndUpload);

        if (TfOverrideLocationOnRead.IsValid) spoolmanManager.OverrideLocationOnRead = viewModel.OverrideLocationOnRead;

        base.OnNavigatingFrom(args);
    }
}