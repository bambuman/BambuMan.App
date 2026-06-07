using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Bambuddy.Api.Api;
using Bambuddy.Api.Client;
using Bambuddy.Api.Extensions;
using Bambuddy.Api.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        public string? ApiKey { get; set; }

        private readonly Lock spoolLock = new();
        private List<SpoolResponse> cachedSpools = [];
        private List<ExternalFilament> bambuLabFilaments = [];

        // The spool + scan info most recently surfaced via OnSpoolFound, retained for UpdateCurrentSpoolAsync.
        private SpoolResponse? currentSpool;
        private BambuFilamentInfo? currentInfo;

        private bool catalogLoaded;
        private bool spoolsLoaded;
        private bool byTagSupported; // server exposes GET /inventory/spools/by-tag -> per-scan lookup, no cache preload

        /// <summary>
        /// All spools cached locally, scanned by tray_uuid (thread-safe; refreshed on init, appended on create).
        /// </summary>
        public List<SpoolResponse> CachedSpools
        {
            get { lock (spoolLock) return cachedSpools; }
            set { lock (spoolLock) cachedSpools = value; }
        }

        #region BaseManager overrides

        public override InventoryBackend Backend => InventoryBackend.Bambuddy;

        public override SpoolEditFields EditFields => new(BuyDate: false, LotNr: false);

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

            await Log(LogLevel.Warning, $"Can't connect to api. Api response: {health.RawContent.Truncated()}");
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
                    if (ApiHost != null)
                    {
                        var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();
                        byTagSupported = await ProbeByTagSupportAsync(inventoryApi);

                        // With by-tag we look up per scan, so the full-list preload is only needed as the
                        // fallback for older servers that don't expose the endpoint.
                        if (!byTagSupported) await LoadSpools();
                    }

                    spoolsLoaded = true;
                    await Log(LogLevel.Information, byTagSupported
                        ? "by-tag endpoint available; skipped spool cache preload"
                        : $"by-tag endpoint unavailable; cached {CachedSpools.Count} spools for fallback");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    await Log(LogLevel.Warning, $"Spool init failed: {ex.Message}");
                }
            }

            return catalogLoaded && spoolsLoaded;
        }

        protected override void ResetInitState()
        {
            catalogLoaded = false;
            spoolsLoaded = false;
            byTagSupported = false;
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
                await Log(LogLevel.Warning, $"Error loading spools. Api response: {result.RawContent.Truncated()}");
            }
        }

        public override async Task<bool> InventorySpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            if (ApiHost == null) return false;

            try
            {
                var (ok, matched) = await ResolveFilament(info);
                if (!ok) return false;

                var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();

                var existing = await FindSpoolByTagAsync(info.TrayUid, info.SerialNumber);

                if (existing == null)
                {
                    var createResult = await inventoryApi.CreateSpoolApiV1InventorySpoolsPostAsync(BuildSpoolCreate(info, matched, price, location));

                    if (!createResult.TryOk(out var created) || created == null)
                    {
                        await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't add spool. Api response: {createResult.RawContent.Truncated()}");
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
                    OnSpoolInventoried(spool, info);
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
                    OnSpoolInventoried(existing, info);
                }

                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Inventory spool");
                return false;
            }
        }

        public override async Task UpdateCurrentSpoolAsync(SpoolEditInput input)
        {
            if (ApiHost == null || currentSpool == null) return;

            try
            {
                var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();

                // current total weight + empty weight -> weight_used (label_weight comes from the stored spool).
                // Bambuddy has no Buy date / Lot nr, so those fields of the input are ignored.
                decimal? weightUsed = null;
                if (input.Weight.HasValue)
                {
                    decimal empty = input.EmptyWeight ?? currentSpool.CoreWeight ?? 0;
                    var label = currentSpool.LabelWeight ?? 0;
                    weightUsed = Math.Max(empty + label - input.Weight.Value, 0);
                }

                var labelWeight = currentSpool.LabelWeight ?? 1000;
                decimal? costPerKg = input.Price.HasValue ? input.Price.Value / (labelWeight / 1000m) : null;

                // Re-apply the Bambu taxonomy + slicer preset from the scan so editing also fixes spools created
                // before this mapping existed (our edit panel doesn't expose these fields). Unset Options when we
                // have no scan info, so the PATCH leaves the stored values untouched.
                Option<string?> material = default, subtype = default, brand = default, slicerFilament = default, slicerFilamentName = default;
                if (currentInfo != null)
                {
                    var derived = DeriveFilament(currentInfo);
                    material = new Option<string?>(derived.material);
                    subtype = new Option<string?>(derived.subtype); // null clears subtype (correct for ASA)
                    brand = new Option<string?>("Bambu");
                    if (derived.slicerFilament != null)
                    {
                        slicerFilament = new Option<string?>(derived.slicerFilament);
                        slicerFilamentName = new Option<string?>(derived.slicerFilamentName);
                    }
                }

                var update = new SpoolUpdate(
                    material: material,
                    subtype: subtype,
                    brand: brand,
                    weightUsed: new Option<decimal?>(weightUsed),
                    coreWeight: new Option<int?>(input.EmptyWeight.HasValue ? (int)input.EmptyWeight.Value : null),
                    slicerFilament: slicerFilament,
                    slicerFilamentName: slicerFilamentName,
                    costPerKg: new Option<decimal?>(costPerKg),
                    storageLocation: new Option<string?>(input.Location));

                var result = await inventoryApi.UpdateSpoolApiV1InventorySpoolsSpoolIdPatchAsync(currentSpool.Id, update);

                if (result.TryOk(out var updated) && updated != null)
                {
                    currentSpool = updated;
                    AddToCache(updated);
                    ShowMessage(false, $"Spool updated, used weight set to {weightUsed:0.#}g");
                    await Log(LogLevel.Success, $"Spool {updated.Id} updated");
                }
                else
                {
                    await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't update spool. Api response: {result.RawContent.Truncated()}");
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

        /// <summary>One-off connection probe for the settings "Test" button (throwaway host, no retained state).</summary>
        public static async Task<(bool ok, string? error)> TestConnectionAsync(string url, string? apiKey)
        {
            try
            {
                var normalized = url.EndsWith("/") ? url[..^1] : url;

                using var host = Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
                {
                    services.AddApi(options =>
                    {
                        options.AddApiHttpClients(client =>
                        {
                            client.BaseAddress = new Uri(normalized);
                            client.Timeout = TimeSpan.FromSeconds(2);
                        });

                        options.AddTokens(new BearerToken(apiKey ?? string.Empty));
                    });
                }).Build();

                var defaultApi = host.Services.GetRequiredService<IDefaultApi>();
                var health = await defaultApi.HealthCheckHealthGetAsync();

                return health.TryOk(out _) ? (true, null) : (false, health.RawContent);
            }
            catch (TaskCanceledException)
            {
                return (false, "Connection timed out");
            }
            catch (HttpRequestException ex)
            {
                return (false, ex.Message);
            }
            catch (UriFormatException)
            {
                return (false, "Invalid URL format");
            }
        }

        private async Task<(bool ok, ExternalFilament? matched)> ResolveFilament(BambuFilamentInfo info)
        {
            var result = await ExternalFilamentMatcher.FindExternalFilament(bambuLabFilaments, info);

            if (result.Count == 1) return (true, result.First());

            var statusLevel = UnknownFilamentEnabled ? ManagerStatusType.Ready : ManagerStatusType.Error;
            var logLevel = UnknownFilamentEnabled ? LogLevel.Warning : LogLevel.Error;
            var message = result.Count == 0 ? "No matching filament found" : "Found more then 1 matching filament";

            await LogAndSetStatus(statusLevel, logLevel, message, new Exception(info.ToDiagnosticJson()));
            PlayErrorTone();

            // When unknown filaments are enabled, proceed with raw tag data (no catalog enrichment).
            return (UnknownFilamentEnabled, null);
        }

        /// <summary>
        /// Resolve a spool for a scanned tag. When the server exposes the by-tag endpoint (detected at init), looks
        /// it up directly via <see cref="GetSpoolByTagAsync"/> (which also matches archived spools); otherwise scans
        /// the in-memory cache preloaded for older Bambuddy servers. A transient by-tag network error propagates to
        /// the caller's handler rather than being treated as "not found" (which would risk creating a duplicate).
        /// </summary>
        private async Task<SpoolResponse?> FindSpoolByTagAsync(string? trayUid, string? tagUid)
            => byTagSupported ? await GetSpoolByTagAsync(trayUid, tagUid) : FindSpoolByTag(trayUid, tagUid);

        /// <summary>
        /// Direct server lookup of a spool by tag via <c>GET /inventory/spools/by-tag</c> (archived included by
        /// default). Returns null when no spool matches the tag; throws on a transient network error so the caller
        /// can abort instead of mistaking it for "not found".
        /// </summary>
        public async Task<SpoolResponse?> GetSpoolByTagAsync(string? trayUid, string? tagUid, bool includeArchived = true)
        {
            if (ApiHost == null || (string.IsNullOrEmpty(trayUid) && string.IsNullOrEmpty(tagUid))) return null;

            var inventoryApi = ApiHost.Services.GetRequiredService<IInventoryApi>();
            var result = await inventoryApi.GetSpoolByTagApiV1InventorySpoolsByTagGetAsync(
                trayUuid: string.IsNullOrEmpty(trayUid) ? default : new Option<string?>(trayUid),
                tagUid: string.IsNullOrEmpty(tagUid) ? default : new Option<string?>(tagUid),
                includeArchived: new Option<bool>(includeArchived));

            return result.TryOk(out var found) ? found : null;
        }

        /// <summary>
        /// One-shot init probe for the by-tag endpoint. A server that has it answers a miss with the endpoint's own
        /// 404 (which names the spool); an older server without the route returns FastAPI's bare
        /// <c>{"detail":"Not Found"}</c>. Fails safe: anything ambiguous (other status, non-JSON, network error)
        /// returns false, so the caller preloads the cache and behaves exactly as before.
        /// </summary>
        private static async Task<bool> ProbeByTagSupportAsync(IInventoryApi inventoryApi)
        {
            try
            {
                var probe = await inventoryApi.GetSpoolByTagApiV1InventorySpoolsByTagGetAsync(
                    trayUuid: new Option<string?>("00000000000000000000000000000000"), // valid 32-hex form, won't match
                    includeArchived: new Option<bool>(false));

                if (probe.TryOk(out _)) return true;

                return (int)probe.StatusCode == 404 && probe.RawContent.Contains("spool", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return false;
            }
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
            var (material, subtype, slicerFilament, slicerFilamentName) = DeriveFilament(info);

            return new SpoolCreate(
                material: material,
                subtype: new Option<string?>(subtype),
                colorName: new Option<string?>(matched?.Name ?? info.Color),
                rgba: new Option<string?>(info.Color),
                brand: new Option<string?>("Bambu"), // Bambuddy's brand for Bambu filaments is "Bambu" (matches its slicer presets)
                labelWeight: new Option<int?>(labelWeight),
                coreWeight: new Option<int?>(250),
                slicerFilament: new Option<string?>(slicerFilament),
                slicerFilamentName: new Option<string?>(slicerFilamentName),
                nozzleTempMin: new Option<int?>((int?)info.MinTemperatureForHotend),
                nozzleTempMax: new Option<int?>((int?)info.MaxTemperatureForHotend),
                costPerKg: new Option<decimal?>(costPerKg),
                dataOrigin: new Option<string?>("nfc_scan"),
                tagType: new Option<string?>("bambu_rfid"),
                storageLocation: new Option<string?>(location));
        }

        /// <summary>
        /// Derive Bambuddy's material / subtype / slicer-preset fields from a scanned tag, mirroring the server's own
        /// <c>create_spool_from_tray</c>: material = filament type; subtype = the detailed type minus the material
        /// prefix ("PLA Wood" → "Wood"; "ASA" → none); slicer_filament = the Bambu filament code, which is the tag's
        /// unique material id with a leading 'G' ("FA16" → "GFA16" = Bambu PLA Wood). Setting slicer_filament on
        /// create stops Bambuddy's edit form demanding a preset (and keeps our brand instead of it forcing "Bambu").
        /// </summary>
        private static (string material, string? subtype, string? slicerFilament, string? slicerFilamentName) DeriveFilament(BambuFilamentInfo info)
        {
            var material = info.FilamentType ?? "Unknown";
            string? subtype = null;
            var detailed = info.DetailedFilamentType;

            if (!string.IsNullOrEmpty(detailed))
            {
                var space = detailed.IndexOf(' ');
                if (space > 0)
                {
                    if (detailed[..space].EqualsCI(material)) subtype = detailed[(space + 1)..]; // "PLA Wood" -> "Wood"
                    else material = detailed;                                                    // "PETG-HF" style
                }
                else if (!detailed.EqualsCI(material))
                {
                    material = detailed;
                }
            }

            // Gradient / dual / tri-color variants aren't in the detailed type; the color-variant code carries them
            // ("A05-M*" = Silk Dual Color, other "*-M*" = Gradient, "*-T*" = Tri Color).
            var variant = info.MaterialVariantIdentifier;
            if (!string.IsNullOrEmpty(variant) && variant.Contains('-'))
            {
                var dash = variant.IndexOf('-');
                var colorCode = variant[(dash + 1)..];
                if (colorCode.StartsWith('M')) subtype = variant[..dash].EqualsCI("A05") ? "Dual Color" : "Gradient";
                else if (colorCode.StartsWith('T')) subtype = "Tri Color";
            }

            // "Aero" foaming variants (e.g. ASA Aero, PLA Aero) carry the variant in the slicer preset (GFB02 / GFA11),
            // so collapse to the base material with no subtype — matching Bambuddy's AMS taxonomy. Hyphenated grades
            // ("PETG-CF", "PA6-GF") don't end in " Aero" and are left intact.
            const string aeroSuffix = " Aero";
            if (!string.IsNullOrEmpty(detailed) && detailed.EndsWith(aeroSuffix, StringComparison.OrdinalIgnoreCase))
            {
                material = detailed[..^aeroSuffix.Length].Trim();
                subtype = null;
            }

            // tray_info_idx = "G" + unique material id (e.g. "FA16" -> "GFA16"); name falls back to "Bambu <detailed>".
            var slicerFilament = string.IsNullOrEmpty(info.UniqueMaterialIdentifier) ? null : $"G{info.UniqueMaterialIdentifier}";
            var slicerFilamentName = slicerFilament == null ? null : (string.IsNullOrEmpty(detailed) ? null : $"Bambu {detailed}");

            return (material, subtype, slicerFilament, slicerFilamentName);
        }

        private void OnSpoolInventoried(SpoolResponse spool, BambuFilamentInfo info)
        {
            currentSpool = spool;
            currentInfo = info;
            RaiseSpoolFound(BuildSpoolFound(spool, info), info);
        }

        private static SpoolFound BuildSpoolFound(SpoolResponse s, BambuFilamentInfo info)
        {
            // current total = label_weight + core_weight - weight_used; convert cost_per_kg back to per-spool price.
            decimal? weight = (s.LabelWeight ?? 0) + (s.CoreWeight ?? 0) - s.WeightUsed.GetValueOrDefault();
            decimal? price = s.CostPerKg.HasValue && s.LabelWeight.HasValue ? s.CostPerKg.Value * (s.LabelWeight.Value / 1000m) : null;

            return new SpoolFound(
                Material: s.Material,
                TrayUid: info.TrayUid ?? s.TrayUuid,
                Weight: weight,
                EmptyWeight: (decimal?)s.CoreWeight,
                Price: price,
                BuyDate: null,
                LotNr: null,
                Location: s.StorageLocation);
        }

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
