using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Bambuddy.Api.Api;
using Bambuddy.Api.Client;
using Bambuddy.Api.Extensions;
using Bambuddy.Api.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ExternalFilament = SpoolMan.Api.Model.ExternalFilament;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared
{
    /// <summary>
    /// Inventory backend for a Bambuddy server. Mirrors <see cref="SpoolmanManager"/>'s narrow surface but talks
    /// to the Bambuddy inventory API. Phase 1 matches a scanned tag against a client-side spool cache (no by-tag
    /// endpoint upstream yet). Auth is the API key sent as <c>Authorization: Bearer bb_xxx</c>, which Bambuddy
    /// accepts as an API key.
    /// </summary>
    public class BambuddyManager(ILogger<BambuddyManager>? logger) : BaseManager(logger)
    {
        public delegate void SpoolFoundEventHandler(BambuddySpoolFound found);

        public event SpoolFoundEventHandler? OnSpoolFound;

        public string? ApiKey { get; set; }

        private readonly Lock spoolLock = new();
        private List<SpoolResponse> cachedSpools = [];
        private List<ExternalFilament> bambuLabFilaments = [];

        private bool catalogLoaded;
        private bool spoolsLoaded;

        /// <summary>
        /// All spools cached locally, scanned by tray_uuid (thread-safe; refreshed on init, appended on create).
        /// </summary>
        public List<SpoolResponse> CachedSpools
        {
            get { lock (spoolLock) return cachedSpools; }
            set { lock (spoolLock) cachedSpools = value; }
        }

        #region BaseManager overrides

        protected override string BackendName => "Bambuddy";

        protected override IHost CreateApiHost(string normalizedApiUrl)
        {
            return Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
                {
                    services.AddApi(options =>
                    {
                        options.AddApiHttpClients(client =>
                        {
                            client.BaseAddress = new Uri(normalizedApiUrl);
                            client.Timeout = TimeSpan.FromSeconds(5);
                        }, builder =>
                        {
                            builder.AddRetryPolicy(3);
                        });

                        // The generated client always pulls a BearerToken from the provider. Bambuddy accepts an
                        // API key sent as "Authorization: Bearer bb_xxx", so register the key as the bearer token.
                        options.AddTokens(new BearerToken(ApiKey ?? string.Empty));
                    });
                })
            .Build();
        }

        protected override async Task<bool> CheckHealthAsync()
        {
            if (ApiHost == null) return IsHealth = false;

            var defaultApi = ApiHost.Services.GetRequiredService<IDefaultApi>();
            var health = await defaultApi.HealthCheckHealthGetAsync();

            if (health.TryOk(out _)) return IsHealth = true;

            await Log(LogLevel.Warning, $"Can't connect to api. Api response: {health.RawContent}");
            return IsHealth = false;
        }

        protected override async Task<bool> LoadInitialDataAsync()
        {
            if (!catalogLoaded)
            {
                bambuLabFilaments = ExternalFilamentMatcher.LoadEmbeddedFilaments();
                catalogLoaded = true;
                await Log(LogLevel.Information, $"Loaded local filaments: {bambuLabFilaments.Count}");
            }

            if (!spoolsLoaded)
            {
                try
                {
                    await LoadSpools();
                    spoolsLoaded = true;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    await Log(LogLevel.Warning, $"LoadSpools failed: {ex.Message}");
                }
            }

            return catalogLoaded && spoolsLoaded;
        }

        protected override void ResetInitState()
        {
            catalogLoaded = false;
            spoolsLoaded = false;
        }

        #endregion

        private async Task LoadSpools()
        {
            if (ApiHost == null) return;

            var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();
            var result = await inventoryApi.ListSpoolsApiV1InventorySpoolsGetAsync(includeArchived: new Option<bool>(false));

            if (result.TryOk(out var spools) && spools != null)
            {
                CachedSpools = spools;
                await Log(LogLevel.Information, $"Loaded spools: {spools.Count}");
            }
            else
            {
                await Log(LogLevel.Warning, $"Error loading spools. Api response: {result.RawContent}");
            }
        }

        public async Task<bool> InventorySpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            if (ApiHost == null) return false;

            try
            {
                var (ok, matched) = await ResolveFilament(info);
                if (!ok) return false;

                var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();

                var existing = FindSpoolByTag(info.TrayUid, info.SerialNumber);

                if (existing == null)
                {
                    var createResult = await inventoryApi.CreateSpoolApiV1InventorySpoolsPostAsync(BuildSpoolCreate(info, matched, price, location));

                    if (!createResult.TryOk(out var created) || created == null)
                    {
                        await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't add spool. Api response: {createResult.RawContent}");
                        return false;
                    }

                    var linkResult = await inventoryApi.LinkTagToSpoolApiV1InventorySpoolsSpoolIdLinkTagPatchAsync(created.Id,
                        new LinkTagRequest(
                            tagUid: new Option<string?>(info.SerialNumber),
                            trayUuid: new Option<string?>(info.TrayUid),
                            tagType: new Option<string?>("bambu_rfid"),
                            dataOrigin: new Option<string?>("nfc_scan")));

                    var spool = linkResult.TryOk(out var linked) && linked != null ? linked : created;

                    AddToCache(spool);
                    ShowMessage(false, $"Spool '{info.TrayUid?.TrimTo(14, "...")}' added");
                    await Log(LogLevel.Success, $"Spool '{info.TrayUid?.TrimTo(14, "...")}' added");
                    OnSpoolFound?.Invoke(Map(spool));
                }
                else
                {
                    if (OverrideLocationOnRead && !string.IsNullOrWhiteSpace(location) && !string.Equals(existing.StorageLocation, location, StringComparison.OrdinalIgnoreCase))
                    {
                        var updateResult = await inventoryApi.UpdateSpoolApiV1InventorySpoolsSpoolIdPatchAsync(existing.Id, new SpoolUpdate(storageLocation: new Option<string?>(location)));

                        if (updateResult.TryOk(out var updated) && updated != null)
                        {
                            existing = updated;
                            AddToCache(updated);
                            await Log(LogLevel.Success, $"Location updated to '{location}' for spool '{info.TrayUid?.TrimTo(14, "...")}'");
                        }
                    }

                    ShowMessage(false, $"Existing spool '{info.TrayUid?.TrimTo(14, "...")}' found");
                    await Log(LogLevel.Success, $"Existing spool '{info.TrayUid?.TrimTo(14, "...")}' found");
                    OnSpoolFound?.Invoke(Map(existing));
                }

                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Inventory spool");
                return false;
            }
        }

        public async Task UpdateSpoolReduced(int id, double? weightUsed, string? location, string? note)
        {
            if (ApiHost == null) return;

            try
            {
                var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();

                var update = new SpoolUpdate(
                    weightUsed: new Option<decimal?>(weightUsed.HasValue ? (decimal)weightUsed.Value : null),
                    storageLocation: new Option<string?>(location),
                    note: new Option<string?>(note));

                var result = await inventoryApi.UpdateSpoolApiV1InventorySpoolsSpoolIdPatchAsync(id, update);

                if (result.TryOk(out var updated) && updated != null)
                {
                    AddToCache(updated);
                    ShowMessage(false, $"Spool updated, used weight set to {weightUsed:0.#}g");
                    await Log(LogLevel.Success, $"Spool {id} updated");
                }
                else
                {
                    await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't update spool. Api response: {result.RawContent}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Update spool");
            }
        }

        /// <summary>Archive (soft-delete) a spool by id. Used for test cleanup; may be surfaced in the UI later.</summary>
        public async Task ArchiveSpoolAsync(int id)
        {
            if (ApiHost == null) return;

            var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();
            await inventoryApi.ArchiveSpoolApiV1InventorySpoolsSpoolIdArchivePostAsync(id);
        }

        private async Task<(bool ok, ExternalFilament? matched)> ResolveFilament(BambuFilamentInfo info)
        {
            var result = await ExternalFilamentMatcher.FindExternalFilament(bambuLabFilaments, info);

            if (result.Count == 1) return (true, result.First());

            var statusLevel = UnknownFilamentEnabled ? ManagerStatusType.Ready : ManagerStatusType.Error;
            var logLevel = UnknownFilamentEnabled ? LogLevel.Warning : LogLevel.Error;
            var message = result.Count == 0 ? "No matching filament found" : "Found more then 1 matching filament";

            await LogAndSetStatus(statusLevel, logLevel, message, new Exception(JsonConvert.SerializeObject(info)));
            PlayErrorTone();

            // When unknown filaments are enabled, proceed with raw tag data (no catalog enrichment).
            return (UnknownFilamentEnabled, null);
        }

        /// <summary>Find a cached spool by tray_uuid, falling back to tag_uid.</summary>
        public SpoolResponse? FindSpoolByTag(string? trayUid, string? tagUid)
        {
            var spools = CachedSpools;

            if (!string.IsNullOrEmpty(trayUid))
            {
                var byTray = spools.FirstOrDefault(x => string.Equals(x.TrayUuid, trayUid, StringComparison.OrdinalIgnoreCase));
                if (byTray != null) return byTray;
            }

            if (!string.IsNullOrEmpty(tagUid))
                return spools.FirstOrDefault(x => string.Equals(x.TagUid, tagUid, StringComparison.OrdinalIgnoreCase));

            return null;
        }

        /// <summary>Map a scanned tag + matched catalog entry to a Bambuddy spool-create body, converting per-spool price to cost_per_kg.</summary>
        public static SpoolCreate BuildSpoolCreate(BambuFilamentInfo info, ExternalFilament? matched, decimal? price, string? location)
        {
            var labelWeight = info.SpoolWeight ?? 1000;
            decimal? costPerKg = price.HasValue ? price.Value / (labelWeight / 1000m) : null;

            return new SpoolCreate(
                material: info.FilamentType ?? matched?.Material ?? "Unknown",
                subtype: new Option<string?>(info.DetailedFilamentType),
                colorName: new Option<string?>(matched?.Name ?? info.Color),
                rgba: new Option<string?>(info.Color),
                brand: new Option<string?>(matched?.Manufacturer ?? "Bambu Lab"),
                labelWeight: new Option<int?>(labelWeight),
                coreWeight: new Option<int?>(250),
                nozzleTempMin: new Option<int?>((int?)info.MinTemperatureForHotend),
                nozzleTempMax: new Option<int?>((int?)info.MaxTemperatureForHotend),
                costPerKg: new Option<decimal?>(costPerKg),
                dataOrigin: new Option<string?>("nfc_scan"),
                tagType: new Option<string?>("bambu_rfid"),
                storageLocation: new Option<string?>(location));
        }

        private static BambuddySpoolFound Map(SpoolResponse s) =>
            new(s.Id, s.Material, s.TrayUuid, s.Brand, s.ColorName, s.WeightUsed, s.LabelWeight, s.CoreWeight);

        private void AddToCache(SpoolResponse spool)
        {
            lock (spoolLock)
            {
                var index = cachedSpools.FindIndex(x => x.Id == spool.Id);
                if (index >= 0) cachedSpools[index] = spool;
                else cachedSpools.Add(spool);
            }
        }
    }
}
