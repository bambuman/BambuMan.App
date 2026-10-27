using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Resolvers;
using Microsoft.AspNetCore.Components;
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
    public const string KeyInventoryBackend = "inventory_backend";
    public const string KeyBambuddyUrl = "bambuddy_url";
    public const string KeyBambuddyApiKey = "bambuddy_api_key";
    public const string KeyStoreSpoolsLocally = "store_spools_locally";

    /// <summary>Raised after a setting is stored, for parts of the ui outside this page that depend on it (the menu).</summary>
    public static event Action? SettingsChanged;

    private IHost? apiHost;
    private string? apiHostUrl;

    private bool consentVisible;
    private bool consentDismissible;
    private bool testingConnection;

    [Inject] private SettingsPageViewModel Vm { get; set; } = null!;
    [Inject] private ILogger<SettingsPage> Logger { get; set; } = null!;
    [Inject] private IInventoryBackendResolver Backends { get; set; } = null!;
    [Inject] private QrScanService QrScan { get; set; } = null!;
    [Inject] private ToastService Toasts { get; set; } = null!;

    /// <summary>
    /// The active inventory backend. An explicit saved choice always wins; with no saved choice, an upgrade from a
    /// Spoolman-only build (a Spoolman URL is already stored) keeps Spoolman, while a fresh install defaults to Bambuddy.
    /// </summary>
    public static InventoryBackend GetInventoryBackend()
    {
        if (Enum.TryParse<InventoryBackend>(Preferences.Default.Get(KeyInventoryBackend, string.Empty), out var backend))
            return backend;

        // No saved choice yet: keep existing Spoolman users (they already have a Spoolman URL) on Spoolman;
        // brand-new installs start on Bambuddy.
        return string.IsNullOrEmpty(Preferences.Default.Get(KeySpoolmanUrl, string.Empty)) ? InventoryBackend.Bambuddy : InventoryBackend.Spoolman;
    }

    /// <summary>
    /// True when the selected backend needs a server url and none is stored. "No backend" is a valid configured state
    /// without one.
    /// </summary>
    public static bool IsServerUrlMissing()
    {
        var backend = GetInventoryBackend();

        if (backend == InventoryBackend.NoBackend) return false;

        var activeUrl = backend == InventoryBackend.Bambuddy
            ? Preferences.Default.Get(KeyBambuddyUrl, string.Empty)
            : Preferences.Default.Get(KeySpoolmanUrl, string.Empty);

        return string.IsNullOrWhiteSpace(activeUrl);
    }

    protected override void OnInitialized()
    {
        try
        {
            Observe(Vm);

            Vm.InventoryBackend = GetInventoryBackend();
            Vm.SpoolmanUrl = Preferences.Default.Get(KeySpoolmanUrl, string.Empty);
            Vm.BambuddyUrl = Preferences.Default.Get(KeyBambuddyUrl, string.Empty);
            Vm.BambuddyApiKey = Preferences.Default.Get(KeyBambuddyApiKey, string.Empty);
            Vm.BuyDate = DateTime.TryParse(Preferences.Default.Get(KeyDefaultBuyDate, string.Empty), CultureInfo.CurrentCulture, out var resultDate) ? resultDate : null;
            Vm.DefaultPrice = decimal.TryParse(Preferences.Default.Get(KeyDefaultPrice, string.Empty), NumberStyles.Any, NumberFormatInfo.CurrentInfo, out var result) ? result : null;
            Vm.DefaultLotNr = Preferences.Default.Get(KeyDefaultLotNr, string.Empty);
            Vm.DefaultLocation = Preferences.Default.Get(KeyDefaultLocation, string.Empty);
            Vm.UnknownFilamentEnabled = Preferences.Default.Get(UnknownFilamentEnabled, true);
            Vm.ShowLogsOnMainPage = Preferences.Default.Get(ShowLogsOnMainPage, true);
            Vm.ShowKeyboardOnSpoolRead = Preferences.Default.Get(ShowKeyboardOnSpoolRead, true);
            Vm.FullTagScanAndUpload = Preferences.Default.Get(FullTagScanAndUpload, false);
            Vm.StoreSpoolsLocally = Preferences.Default.Get(KeyStoreSpoolsLocally, false);
            Vm.OverrideLocationOnRead = Backends.Resolve(GetInventoryBackend()).OverrideLocationOnRead;

            // First visit: ask about the research upload before anything else.
            if (!Preferences.Default.Get(TagUploadConsentShown, false))
            {
                consentDismissible = false;
                consentVisible = true;
            }

            _ = LoadLocationsAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error in OnInitialized");
        }
    }

    #region Persistence

    /// <summary>
    /// Stores every field that passes its validation. Called after each change, so leaving the page — by the menu, the
    /// back button or closing the app — never loses an edit.
    /// </summary>
    private void Persist()
    {
        Preferences.Default.Set(KeyInventoryBackend, Vm.InventoryBackend.ToString());

        // One visible URL field proxies to the active backend; persist both so the inactive one isn't lost.
        // With no backend selected the field is hidden, so persist the stored urls anyway — switching to that mode
        // must not discard them.
        if (Vm.IsNoBackend || Vm.ValidateServerUrl())
        {
            Preferences.Default.Set(KeySpoolmanUrl, Vm.SpoolmanUrl ?? string.Empty);
            Preferences.Default.Set(KeyBambuddyUrl, Vm.BambuddyUrl ?? string.Empty);
        }

        if (!Vm.IsBambuddy || Vm.ValidateBambuddyApiKey()) Preferences.Default.Set(KeyBambuddyApiKey, Vm.BambuddyApiKey ?? string.Empty);

        if (Vm.BuyDate == null) Preferences.Default.Remove(KeyDefaultBuyDate);
        else Preferences.Default.Set(KeyDefaultBuyDate, $"{Vm.BuyDate:yyyy-MM-dd}");

        if (Vm.ValidateDefaultPrice())
        {
            if (Vm.DefaultPrice == null) Preferences.Default.Remove(KeyDefaultPrice);
            else Preferences.Default.Set(KeyDefaultPrice, $"{Vm.DefaultPrice:0.00}");
        }

        Preferences.Default.Set(KeyDefaultLotNr, Vm.DefaultLotNr ?? string.Empty);
        Preferences.Default.Set(KeyDefaultLocation, Vm.DefaultLocation ?? string.Empty);
        Preferences.Default.Set(UnknownFilamentEnabled, Vm.UnknownFilamentEnabled);
        Preferences.Default.Set(ShowLogsOnMainPage, Vm.ShowLogsOnMainPage);
        Preferences.Default.Set(ShowKeyboardOnSpoolRead, Vm.ShowKeyboardOnSpoolRead);
        Preferences.Default.Set(FullTagScanAndUpload, Vm.FullTagScanAndUpload);
        Preferences.Default.Set(KeyStoreSpoolsLocally, Vm.StoreSpoolsLocally);

        foreach (var manager in Backends.All) manager.OverrideLocationOnRead = Vm.OverrideLocationOnRead;

        SettingsChanged?.Invoke();
    }

    /// <summary>Applies a change made outside a bound input (switch, clear button) and stores it.</summary>
    private void Set(Action change)
    {
        change();
        Persist();
    }

    private void SelectBackend(InventoryBackend backend)
    {
        if (Vm.InventoryBackend == backend) return;

        // The url field now shows another backend's url; its old message doesn't apply to it.
        Vm.ServerUrlError = null;
        Vm.BambuddyApiKeyError = null;

        Vm.InventoryBackend = backend;
        Preferences.Default.Set(KeyInventoryBackend, backend.ToString());
        SettingsChanged?.Invoke();

        _ = LoadLocationsAsync();
    }

    private void ClearApiKey() => Set(() => Vm.BambuddyApiKey = null);

    #endregion

    #region QR scan

    private async Task ScanQrAsync(string target)
    {
        try
        {
            // URLs are validated against the URL regex; API keys (and other free text) are not.
            var value = await QrScan.ScanAsync(requireUrl: target.EndsWith("url", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(value)) return;

            Vm.ApplyScanResult(target, value);
            Persist();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error scanning qrcode");
        }
    }

    #endregion

    #region Consent

    private void ShowConsentInfo()
    {
        consentDismissible = true;
        consentVisible = true;
    }

    private void SetFullTagScanAndUpload(bool value) => Set(() => Vm.FullTagScanAndUpload = value);

    private Task OnConsentResultAsync(bool accepted)
    {
        consentVisible = false;

        Preferences.Default.Set(TagUploadConsentShown, true);
        SetFullTagScanAndUpload(accepted);

        return Task.CompletedTask;
    }

    #endregion

    #region Locations

    private async Task LoadLocationsAsync()
    {
        try
        {
            // "No backend" promises nothing is sent to an inventory server, so its suggestions are the stored spools' locations.
            if (Vm.IsNoBackend)
            {
                Vm.ExistingLocations = Backends.Resolve(InventoryBackend.NoBackend).ExistingLocations;
                return;
            }

            if (!EnsureApiHost()) return;

            await FetchLocationsAsync();
        }
        catch (Exception)
        {
            //ignore
        }
    }

    private async Task FetchLocationsAsync()
    {
        var settingApi = apiHost!.Services.GetRequiredService<ISettingApi>();
        var locationsRequest = await settingApi.GetSettingSettingKeyGetOrDefaultAsync("locations");

        if (locationsRequest != null && locationsRequest.TryOk(out var locations))
        {
            Vm.ExistingLocations = JsonConvert.DeserializeObject<string[]>(locations.Value) ?? [];
            Vm.LocationsFetched = true;
        }
    }

    private bool EnsureApiHost()
    {
        if (string.IsNullOrEmpty(Vm.SpoolmanUrl)) return false;

        if (apiHost != null && apiHostUrl == Vm.SpoolmanUrl) return true;

        apiHost = CreateSpoolmanHost(Vm.SpoolmanUrl, withRetry: true);
        apiHostUrl = Vm.SpoolmanUrl;
        return true;
    }

    private static IHost CreateSpoolmanHost(string url, bool withRetry)
    {
        var apiUrl = url.EndsWith('/') ? url[..^1] : url;
        apiUrl = apiUrl.Contains("api/v1") ? apiUrl : $"{apiUrl}/api/v1";

        return Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
            {
                services.AddApi(options =>
                {
                    options.AddApiHttpClients(client =>
                    {
                        client.BaseAddress = new Uri(apiUrl);
                        client.Timeout = TimeSpan.FromSeconds(2);

                        if (!string.IsNullOrEmpty(client.BaseAddress.UserInfo))
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client.BaseAddress.UserInfo)));
                    }, builder =>
                    {
                        if (withRetry) builder.AddRetryPolicy(3);
                    });
                });
            })
            .Build();
    }

    private async Task RefreshLocationsAsync()
    {
        try
        {
            if (!EnsureApiHost())
            {
                Toasts.Show("Set a Spoolman URL first");
                return;
            }

            await FetchLocationsAsync();

            Toasts.Show("Locations refreshed", ToastKind.Success);
        }
        catch (Exception)
        {
            Toasts.Show("Failed to refresh locations", ToastKind.Error);
        }
    }

    #endregion

    #region Test connection

    private async Task TestConnectionAsync()
    {
        testingConnection = true;

        try
        {
            if (Vm.IsBambuddy) await TestBambuddyUrlAsync();
            else await TestSpoolmanUrlAsync();
        }
        finally
        {
            testingConnection = false;
        }
    }

    private async Task TestSpoolmanUrlAsync()
    {
        try
        {
            var url = Vm.SpoolmanUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                Toasts.Show("Spoolman URL is empty", ToastKind.Error);
                return;
            }

            using var testHost = CreateSpoolmanHost(url, withRetry: false);

            var defaultApi = testHost.Services.GetRequiredService<IDefaultApi>();
            var healthResult = await defaultApi.HealthHealthGetAsync();

            if (healthResult.TryOk(out _)) Toasts.Show("Connection successful", ToastKind.Success, true);
            else Toasts.Show($"Connection failed: {healthResult.RawContent}", ToastKind.Error, true);
        }
        catch (TaskCanceledException)
        {
            Toasts.Show("Connection timed out — check the URL and ensure Spoolman is running", ToastKind.Error, true);
        }
        catch (HttpRequestException)
        {
            Toasts.Show("Could not reach Spoolman — check the URL and network connection", ToastKind.Error, true);
        }
        catch (UriFormatException)
        {
            Toasts.Show("Invalid URL format", ToastKind.Error, true);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Test Spoolman URL failed: {Message}", ex.Message);
            Toasts.Show($"Connection failed: {ex.Message}", ToastKind.Error, true);
        }
    }

    private async Task TestBambuddyUrlAsync()
    {
        try
        {
            var url = Vm.BambuddyUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                Toasts.Show("Bambuddy URL is empty", ToastKind.Error);
                return;
            }

            var (ok, error) = await BambuddyManager.TestConnectionAsync(url, Vm.BambuddyApiKey);

            if (ok) Toasts.Show("Connection successful", ToastKind.Success, true);
            else Toasts.Show($"Connection failed: {error}", ToastKind.Error, true);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Test Bambuddy URL failed: {Message}", ex.Message);
            Toasts.Show($"Connection failed: {ex.Message}", ToastKind.Error, true);
        }
    }

    #endregion

    public override void Dispose()
    {
        apiHost?.Dispose();
        base.Dispose();
    }
}
