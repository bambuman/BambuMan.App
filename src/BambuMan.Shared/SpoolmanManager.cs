using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SpoolMan.Api.Api;
using SpoolMan.Api.Client;
using SpoolMan.Api.Extensions;
using SpoolMan.Api.Model;
using System.Net.Http.Headers;
using System.Text;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared
{
    public class SpoolmanManager(ILogger<SpoolmanManager>? logger) : BaseManager(logger)
    {
        public const string DefaultBambuLabVendor = "Bambu Lab";
        public const string ExtraBuyDate = "buy_date";
        public const string ExtraTag = "tag";
        public const string ExtraProductionDateTime = "production_time";

        private bool defaultsChecked;
        private bool localFilamentsLoaded;
        private bool locationsLoaded;
        private readonly Lock filamentLock = new();
        private readonly Lock spoolLock = new();

        // The spool most recently surfaced via OnSpoolFound, retained for UpdateCurrentSpoolAsync.
        private Spool? currentSpool;
        private BambuFilamentInfo? currentInfo;

        /// <summary>The full Spool most recently surfaced via OnSpoolFound (for consumers needing richer data than <see cref="SpoolFound"/>, e.g. the desktop app).</summary>
        public Spool? CurrentSpool => currentSpool;

        public SpoolManDefaults Defaults { get; } = new();

        public Vendor? BambuLabsVendor { get; set; }

        /// <summary>
        /// All external filaments
        /// </summary>
        public List<ExternalFilament> AllExternalFilaments { get; set; } = [];

        /// <summary>
        /// Bambu lab's external filaments (thread-safe; updated from background API fetch)
        /// </summary>
        public List<ExternalFilament> BambuLabExternalFilaments
        {
            get { lock (filamentLock) return field; }
            set { lock (filamentLock) field = value; }
        } = [];

        private List<Spool> cachedSpools = [];

        /// <summary>
        /// All spools cached locally (thread-safe; updated from background API fetch)
        /// </summary>
        public List<Spool> CachedSpools
        {
            get { lock (spoolLock) return cachedSpools; }
            set { lock (spoolLock) cachedSpools = value; }
        }

        /// <summary>
        /// Unknown filament, if no filament is found or multiple result where found, return this
        /// </summary>
        public ExternalFilament UnknownFilament { get; } = ExternalFilamentMatcher.GenerateUnknownFilament();

        #region BaseManager overrides

        public override InventoryBackend Backend => InventoryBackend.Spoolman;

        public override SpoolEditFields EditFields => new(BuyDate: true, LotNr: true);

        protected override string NormalizeApiUrl(string apiUrl)
        {
            var url = apiUrl.EndsWith("/") ? apiUrl.Substring(0, apiUrl.Length - 1) : apiUrl;
            return url.Contains("api/v1") ? url : $"{url}/api/v1";
        }

        protected override IHost CreateApiHost(string normalizedApiUrl)
        {
            return Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
                {
                    services.AddApi(options =>
                    {
                        options.AddApiHttpClients(client =>
                        {
                            var uri = new Uri(normalizedApiUrl);

                            // If credentials are embedded in the URL (https://user:pass@host), use them
                            // for Basic auth but strip them from the base address, so they can't leak into
                            // logs/telemetry via a request URI in an exception.
                            if (!string.IsNullOrEmpty(uri.UserInfo))
                            {
                                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(uri.UserInfo)));
                                uri = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri;
                            }

                            client.BaseAddress = uri;
                            client.Timeout = TimeSpan.FromSeconds(5);

                        }, builder =>
                        {
                            builder.AddRetryPolicy(3);
                        });
                    });
                })
            .Build();
        }

        protected override async Task<bool> CheckHealthAsync()
        {
            if (ApiHost == null) return IsHealth = false;

            var defaultApi = ApiHost.Services.GetRequiredService<IDefaultApi>();
            var health = await defaultApi.HealthHealthGetAsync();

            if (health.TryOk(out var check))
            {
                if (check.Status == "healthy") return IsHealth = true;

                await Log(LogLevel.Warning, $"Api connected and health check returned: {check.Status}");
            }
            else
            {
                await Log(LogLevel.Warning, $"Can't connect to api. Api response: {health.RawContent.Truncated()}");
            }

            return IsHealth = false;
        }

        protected override async Task<bool> LoadInitialDataAsync()
        {
            await TryCheckDefaultValuesAsync();
            await TryLoadLocalFilamentsAsync();
            await TryLoadLocationsAsync();

            return defaultsChecked && localFilamentsLoaded;
        }

        protected override void ResetInitState()
        {
            defaultsChecked = false;
            localFilamentsLoaded = false;
            locationsLoaded = false;
        }

        protected override Task OnReady()
        {
            _ = LoadExternalFilamentsInBackground();
            _ = LoadSpoolsInBackground();

            return Task.CompletedTask;
        }

        #endregion

        private async Task TryCheckDefaultValuesAsync()
        {
            if (defaultsChecked) return;

            try
            {
                await CheckDefaultValues();
                defaultsChecked = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Log(LogLevel.Warning, $"CheckDefaultValues failed: {ex.Message}");
            }
        }

        private async Task TryLoadLocalFilamentsAsync()
        {
            if (localFilamentsLoaded) return;

            try
            {
                await LoadLocalFilaments();
                localFilamentsLoaded = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Log(LogLevel.Warning, $"LoadLocalFilaments failed: {ex.Message}");
            }
        }

        private async Task TryLoadLocationsAsync()
        {
            if (locationsLoaded) return;

            try
            {
                await LoadLocations();
                locationsLoaded = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Log(LogLevel.Warning, $"LoadLocations failed: {ex.Message}");
            }
        }

        public SpoolmanManager Test()
        {
            ExternalFilamentMatcher.ExtendWithMissingFilaments(BambuLabExternalFilaments);
            return this;
        }

        private async Task CheckDefaultValues()
        {
            if (ApiHost == null) return;

            await LogAndSetStatus(ManagerStatusType.CheckingDefaults, LogLevel.Information, "Checking default settings");

            #region Default vendor

            var vendorApi = ApiHost.Services.GetRequiredService<IVendorApi>();

            var bambuLabsVendor = await vendorApi.FindVendorVendorGetAsync(name: new Option<string?>(DefaultBambuLabVendor));

            //let's add 'Bambu Lab' as vendor

            if (bambuLabsVendor.TryOk(out var vendors))
            {
                if (vendors.Count >= 1)
                {
                    BambuLabsVendor = vendors.First();
                    await Log(LogLevel.Information, $"Default '{DefaultBambuLabVendor}' vendor exists");
                    Defaults.VendorExists = true;
                }
                else
                {
                    var vendorAddResponse = await vendorApi.AddVendorVendorPostAsync(new VendorParameters(DefaultBambuLabVendor, emptySpoolWeight: new Option<decimal?>(250)));

                    if (vendorAddResponse.TryOk(out var addedVendor))
                    {
                        BambuLabsVendor = addedVendor;
                        await Log(LogLevel.Information, $"Created default '{DefaultBambuLabVendor}' vendor");
                        Defaults.VendorExists = true;
                    }
                    else
                    {
                        await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't add default '{DefaultBambuLabVendor}' vendor. Api response: {vendorAddResponse.RawContent.Truncated()}");
                        return;
                    }
                }
            }
            else
            {
                await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't add default '{DefaultBambuLabVendor}' vendor. Api response: {bambuLabsVendor.RawContent.Truncated()}");
                return;
            }

            #endregion

            #region Extra fields

            var fieldApi = ApiHost.Services.GetRequiredService<IFieldApi>();

            var extraFields = new List<ExtraFieldModel>
            {
                new (true, EntityType.spool, 1, ExtraBuyDate, "Buy Date", ExtraFieldType.Datetime),
                new (false, EntityType.spool, 2, ExtraProductionDateTime, "Production Time", ExtraFieldType.Datetime),
                new (false, EntityType.spool, 3, "active_tray", "Active Tray", ExtraFieldType.Text),
                new (false, EntityType.spool, 4, ExtraTag, "Tag", ExtraFieldType.Text),

                new (false, EntityType.filament, 1, "type", "Type", ExtraFieldType.Choice) { Choices =  ["Silk", "Basic", "High Speed", "Matte", "Plus", "Flexible", "Translucent"], DefaultValue = "\"Basic\""},
                new (false, EntityType.filament, 2, "nozzle_temperature", "Nozzle Temperature", ExtraFieldType.IntegerRange) { Unit = "°C", DefaultValue = "[190,300]" }
            };

            foreach (var group in extraFields.GroupBy(x => x.EntryType))
            {
                var existingFieldsQuery = await fieldApi.GetExtraFieldsFieldEntityTypeGetAsync(group.Key);

                if (existingFieldsQuery.TryOk(out var existingFields))
                {
                    foreach (var extraFieldModel in group)
                    {
                        if (existingFields.Any(x => x.Key == extraFieldModel.Key))
                        {
                            await Log(LogLevel.Information, $"Extra field {extraFieldModel.Key} exists, skipping");
                            continue;
                        }

                        var entry = new ExtraFieldParameters(
                            extraFieldModel.Name,
                            extraFieldModel.FieldType,
                            order: new Option<int?>(extraFieldModel.Order),
                            choices: new Option<List<string>?>(extraFieldModel.Choices?.ToList()),
                            multiChoice: extraFieldModel.FieldType == ExtraFieldType.Choice ? new Option<bool?>(extraFieldModel.MultiChoice) : null,
                            unit: new Option<string?>(extraFieldModel.Unit),
                            defaultValue: new Option<string?>(extraFieldModel.DefaultValue)
                            );

                        var addFieldQuery = await fieldApi.AddOrUpdateExtraFieldFieldEntityTypeKeyPostAsync(extraFieldModel.EntryType, extraFieldModel.Key, entry);

                        if (addFieldQuery.TryOk(out _))
                        {
                            await Log(LogLevel.Information, $"Created extra field '{extraFieldModel.Key}'");
                        }
                        else
                        {
                            await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't add extra field '{extraFieldModel.Key}'. Api response: {addFieldQuery.RawContent.Truncated()}");
                            return;
                        }
                    }

                    Defaults.ExtraFieldsAdded = true;
                }
                else
                {
                    await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't get existing extra fields. Api response: {existingFieldsQuery.RawContent.Truncated()}");
                    return;
                }
            }

            #endregion

            await LogAndSetStatus(ManagerStatusType.DefaultsOk, LogLevel.Success, "Spoolman default setting ok");
        }

        private async Task LoadLocalFilaments()
        {
            var localFilaments = ExternalFilamentMatcher.LoadEmbeddedFilaments();
            BambuLabExternalFilaments = localFilaments;
            await Log(LogLevel.Information, $"Loaded local filaments: {localFilaments.Count}");
        }

        private async Task LoadExternalFilamentsInBackground()
        {
            try
            {
                if (ApiHost == null) return;

                var externalApi = ApiHost.Services.GetRequiredService<IExternalApi>();
                var allExternalFilaments = await externalApi.GetAllExternalFilamentsExternalFilamentGetAsync();

                if (allExternalFilaments.TryOk(out var list))
                {
                    AllExternalFilaments = list;

                    // Build merged list: API filaments as base, extend with local-only entries
                    var apiBambuFilaments = list.Where(x => x.Manufacturer == DefaultBambuLabVendor).ToList();
                    ExternalFilamentMatcher.ExtendWithMissingFilaments(apiBambuFilaments);

                    // Swap atomically
                    BambuLabExternalFilaments = apiBambuFilaments;

                    await Log(LogLevel.Information, $"Background: loaded external filaments: {AllExternalFilaments.Count}, merged Bambu Lab: {apiBambuFilaments.Count}");
                }
                else
                {
                    await Log(LogLevel.Warning, $"Background: error loading external filaments. Api response: {allExternalFilaments.RawContent.Truncated()}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Log(LogLevel.Information, $"Background: network error loading external filaments: {ex.Message}");
            }
            catch (Exception e)
            {
                await Log(LogLevel.Information, $"Background: error loading external filaments: {e.Message}");
                logger?.LogWarning(e, "Background: error loading external filaments");
            }
        }

        private async Task LoadSpoolsInBackground()
        {
            try
            {
                if (ApiHost == null) return;

                var spoolApi = ApiHost.Services.GetRequiredService<ISpoolApi>();
                var spoolQuery = await spoolApi.FindSpoolSpoolGetAsync();

                if (spoolQuery.TryOk(out var spools))
                {
                    CachedSpools = spools;
                    await Log(LogLevel.Information, $"Background: loaded spools: {spools.Count}");
                }
                else
                {
                    await Log(LogLevel.Warning, $"Background: error loading spools. Api response: {spoolQuery.RawContent.Truncated()}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Log(LogLevel.Information, $"Background: network error loading spools: {ex.Message}");
            }
            catch (Exception e)
            {
                await Log(LogLevel.Information, $"Background: error loading spools: {e.Message}");
                logger?.LogWarning(e, "Background: error loading spools");
            }
        }

        private void UpdateCachedSpool(Spool spool)
        {
            lock (spoolLock)
            {
                var index = cachedSpools.FindIndex(x => x.Id == spool.Id);
                if (index >= 0) cachedSpools[index] = spool;
                else cachedSpools.Add(spool);
            }
        }

        public override async Task RefreshLocationsAsync()
        {
            try
            {
                await LoadLocations();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await Log(LogLevel.Warning, $"RefreshLocations failed: {ex.Message}");
            }
        }

        private async Task LoadLocations()
        {
            if (ApiHost == null) return;

            #region Load locations

            var settingApi = ApiHost.Services.GetRequiredService<ISettingApi>();

            var locationsRequest = await settingApi.GetSettingSettingKeyGetOrDefaultAsync("locations");

            if (locationsRequest != null && locationsRequest.TryOk(out var locations))
            {
                ExistingLocations = JsonConvert.DeserializeObject<string[]>(locations.Value) ?? [];

                RaiseLocationsLoaded();
            }

            #endregion
        }

        private void TrackNewLocation(string? location)
        {
            if (string.IsNullOrWhiteSpace(location)) return;
            if (ExistingLocations.Any(l => l.EqualsCI(location))) return;

            ExistingLocations = [.. ExistingLocations, location];
            RaiseLocationsLoaded();
        }

        public override async Task<bool> InventorySpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            if (ApiHost == null) return false;

            try
            {
                var result = true;
                var externalFilament = await FindExternalFilament(info);

                switch (UnknownFilamentEnabled)
                {
                    case false when externalFilament == null:
                        return false;
                    case true when externalFilament == null:
                        externalFilament = UnknownFilament;
                        result = false;
                        break;
                }

                await Log(LogLevel.Debug, externalFilament.ToString());

                var filament = await AddOrUpdateFilament(externalFilament, price, info);
                if (filament == null) return false;

                var spoolApi = ApiHost.Services.GetRequiredService<ISpoolApi>();

                // Try local cache first
                var cachedSpool = CachedSpools.FirstOrDefault(x => x.Extra.TryGetValue(ExtraTag, out var value) && value.Equals($"\"{info.TrayUid}\"", StringComparison.CurrentCultureIgnoreCase));

                Spool? spool;
                if (cachedSpool != null)
                {
                    // Found in local cache — fetch fresh data by ID
                    var spoolByIdQuery = await spoolApi.GetSpoolSpoolSpoolIdGetAsync(cachedSpool.Id);
                    if (spoolByIdQuery.TryOk(out var freshSpool))
                    {
                        spool = freshSpool;
                        UpdateCachedSpool(freshSpool);
                    }
                    else
                    {
                        await Log(LogLevel.Warning, $"Cached spool {cachedSpool.Id} not found on server, falling back to full query");
                        spool = null;
                    }
                }
                else
                {
                    // No local match — full query to Spoolman
                    var spoolQuery = await spoolApi.FindSpoolSpoolGetAsync(filamentId2: new Option<string?>($"{filament.Id}"), allowArchived: new Option<bool>(true));

                    if (spoolQuery.TryOk(out var spools))
                        spool = spools.FirstOrDefault(x => x.Extra.TryGetValue(ExtraTag, out var value) && value.Equals($"\"{info.TrayUid}\"", StringComparison.CurrentCultureIgnoreCase));
                    else
                    {
                        await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't load existing spools. Api response: {spoolQuery.RawContent.Truncated()}");
                        return false;
                    }
                }

                if (spool == null) await AddSpool(info, buyDate, price, lotNr, location, filament, spoolApi);
                else
                {
                    if (OverrideLocationOnRead && !string.IsNullOrWhiteSpace(location) && !string.Equals(spool.Location, location, StringComparison.OrdinalIgnoreCase))
                    {
                        spool = await UpdateSpoolLocation(spool, location, spoolApi);
                        await Log(LogLevel.Success, $"Location updated to '{location}' for spool '{info.TrayUid?.TrimTo(14, "...")}'");
                    }

                    ShowMessage(false, $"Existing spool '{info.TrayUid?.TrimTo(14, "...")}' fount");
                    await Log(LogLevel.Success, $"Existing spool '{info.TrayUid?.TrimTo(14, "...")}' fount");
                    OnSpoolInventoried(spool, info);
                }

                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Inventory spool");
                return false;
            }
        }

        public async Task<ExternalFilament?> FindExternalFilament(BambuFilamentInfo info)
        {
            var result = await ExternalFilamentMatcher.FindExternalFilament(BambuLabExternalFilaments, info);

            var spoolmanErrorLevel = UnknownFilamentEnabled ? ManagerStatusType.Ready : ManagerStatusType.Error;
            var logLevel = UnknownFilamentEnabled ? LogLevel.Warning : LogLevel.Error;

            switch (result.Count)
            {
                case > 1:
                    {
                        foreach (var item in result)
                        {
                            await Log(LogLevel.Debug, item.ToString());
                        }

                        await LogAndSetStatus(spoolmanErrorLevel, logLevel, "Found more then 1 matching filament", new Exception($"{info.ToDiagnosticJson()}\r\n{string.Join("\t\n", result.Select(x => x.ToString()))}"));
                        PlayErrorTone();
                        return null;
                    }
                case 0:
                    await LogAndSetStatus(spoolmanErrorLevel, logLevel, "No matching filament found", new Exception($"{info.ToDiagnosticJson()}"));
                    PlayErrorTone();
                    return null;
            }

            return result.First();
        }

        private async Task<Filament?> AddOrUpdateFilament(ExternalFilament externalFilament, decimal? price, BambuFilamentInfo info)
        {
            if (ApiHost == null) return null;

            try
            {
                var filamentApi = ApiHost.Services.GetRequiredService<IFilamentApi>();

                var filamentQuery = await filamentApi.FindFilamentsFilamentGetAsync(externalId: externalFilament.Id);

                if (filamentQuery.TryOk(out var list))
                {
                    if (list.Count != 0) return list.First();

                    var filamentPost = new FilamentParameters(
                        externalFilament.Density,
                        externalFilament.Diameter,
                        name: new Option<string?>(externalFilament.Name),
                        vendorId: new Option<int?>(BambuLabsVendor?.Id),
                        material: new Option<string?>(externalFilament.Material),
                        price: new Option<decimal?>(price),
                        weight: new Option<decimal?>(externalFilament.Weight),
                        spoolWeight: new Option<decimal?>(externalFilament.SpoolWeight),
                        articleNumber: new Option<string?>(info.SkuStart),
                        //comment: new Option<string?>(externalFilament.CommentOption),
                        settingsExtruderTemp: new Option<int?>(externalFilament.ExtruderTemp),
                        settingsBedTemp: new Option<int?>(externalFilament.BedTemp),
                        colorHex: new Option<string?>(externalFilament.ColorHex),
                        multiColorHexes: new Option<string?>(externalFilament.ColorHexes != null ? string.Join(",", externalFilament.ColorHexes) : null),
                        multiColorDirection: new Option<MultiColorDirectionInput?>((MultiColorDirectionInput?)externalFilament.MultiColorDirection),
                        externalId: new Option<string?>(externalFilament.Id),
                        extra: new Option<Dictionary<string, string>?>(new Dictionary<string, string>
                        {
                            { "nozzle_temperature", $"[{info.MinTemperatureForHotend},{info.MaxTemperatureForHotend}]" }
                        })
                    );

                    var filamentAddResult = await filamentApi.AddFilamentFilamentPostAsync(filamentPost);

                    if (filamentAddResult.TryOk(out var addedFilament)) return addedFilament;

                    await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't add filament. Api response: {filamentAddResult.RawContent.Truncated()}");
                    return null;
                }

                await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't load existing filaments. Api response: {filamentQuery.RawContent.Truncated()}");
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Add/update filament");
                return null;
            }
        }

        private async Task AddSpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location, Filament filament, ISpoolApi spoolApi)
        {
            try
            {
                var extraValues = new Dictionary<string, string>
                {
                    [ExtraProductionDateTime] = $"\"{info.ProductionDateTime:yyyy-MM-ddZHH:mm:ss}\"",
                    [ExtraTag] = $"\"{info.TrayUid}\""
                };

                if (buyDate != null) extraValues[ExtraBuyDate] = $"\"{buyDate:yyyy-MM-dd}Z00:00:00\"";

                var comment = filament.ExternalId == UnknownFilament.Id ? $"Filament: {info.DetailedFilamentType}, Color: #{info.Color}, Spool weight: {info.SpoolWeight:0.#}g" : "";

                var spoolParams = new SpoolParameters(filament.Id,
                    //firstUsed: new Option<string?>(externalFilament.FirstUsed),
                    //lastUsed: new Option<string?>(externalFilament.LastUsed),
                    price: new Option<decimal?>(price),
                    initialWeight: new Option<decimal?>(info.SpoolWeight),
                    spoolWeight: new Option<decimal?>(filament.SpoolWeight),
                    //remainingWeight: new Option<string?>(externalFilament.RemainingWeight),
                    //usedWeight: new Option<string?>(externalFilament.UsedWeight),
                    location: new Option<string?>(location),
                    lotNr: new Option<string?>(lotNr),
                    comment: new Option<string?>(comment),
                    //archived: new Option<string?>(externalFilament.Archived),
                    extra: new Option<Dictionary<string, string>?>(extraValues)
                );

                var spoolAddResult = await spoolApi.AddSpoolSpoolPostAsync(spoolParams);

                if (spoolAddResult.TryOk(out var addedSpool))
                {
                    UpdateCachedSpool(addedSpool);
                    TrackNewLocation(location);
                    ShowMessage(false, $"Spool '{info.TrayUid?.TrimTo(14, "...")}' added");
                    await Log(LogLevel.Success, $"Spool '{info.TrayUid?.TrimTo(14, "...")}' added");
                    OnSpoolInventoried(addedSpool, info);
                }
                else
                {
                    await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't add spool. Api response: {spoolAddResult.RawContent.Truncated()}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Add spool");
            }
        }

        public async Task UpdateSpool(Spool currentSpool, DateTime? buyDate, decimal? price, string? lotNr, string? location,
            decimal emptyWeight, decimal initialWeight, decimal spoolWeight, string? trayUid = null, DateTime? productionDateTime = null)
        {
            if (ApiHost == null) return;

            try
            {
                var spoolApi = ApiHost.Services.GetRequiredService<ISpoolApi>();

                trayUid ??= currentSpool.Extra.TryGetValue(ExtraTag, out var tagOut) ? tagOut.Replace("\"", "") : string.Empty;

                var usedWeight = Math.Max(emptyWeight + initialWeight - spoolWeight, 0);

                var extraValues = currentSpool.Extra;

                if (productionDateTime != null) extraValues[ExtraProductionDateTime] = $"\"{productionDateTime:yyyy-MM-ddZHH:mm:ss}\"";

                extraValues[ExtraBuyDate] = buyDate == null ? "\"\"" : $"\"{buyDate:yyyy-MM-dd}Z00:00:00\"";

                extraValues[ExtraTag] = $"\"{trayUid}\"";

                var spoolUpdateParams = new SpoolUpdateParameters(
                    price: new Option<decimal?>(price),
                    location: new Option<string?>(location),
                    lotNr: new Option<string?>(lotNr),
                    initialWeight: new Option<decimal?>(initialWeight),
                    spoolWeight: new Option<decimal?>(emptyWeight),
                    usedWeight: new Option<decimal?>(usedWeight),
                    extra: new Option<Dictionary<string, string>?>(extraValues)
                );

                var spoolUpdateResult = await spoolApi.UpdateSpoolSpoolSpoolIdPatchAsync(currentSpool.Id, spoolUpdateParams);

                if (spoolUpdateResult.TryOk(out var updatedSpool))
                {
                    UpdateCachedSpool(updatedSpool);
                    TrackNewLocation(location);
                    ShowMessage(false, $"Spool '{trayUid}' updated, used weight set to {usedWeight:0.#}g");
                    await Log(LogLevel.Success, $"Spool '{trayUid}' updated, used weight set to {usedWeight:0.#}g");
                }
                else
                {
                    await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, $"Can't update spool. Api response: {spoolUpdateResult.RawContent.Truncated()}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Update spool");
            }
        }

        private async Task<Spool> UpdateSpoolLocation(Spool spool, string location, ISpoolApi spoolApi)
        {
            var spoolUpdateParams = new SpoolUpdateParameters(
                location: new Option<string?>(location)
            );

            var result = await spoolApi.UpdateSpoolSpoolSpoolIdPatchAsync(spool.Id, spoolUpdateParams);

            if (result.TryOk(out var updatedSpool))
            {
                UpdateCachedSpool(updatedSpool);
                TrackNewLocation(location);
                return updatedSpool;
            }

            await Log(LogLevel.Error, $"Failed to update location: {result.RawContent.Truncated()}");
            return spool;
        }

        private void OnSpoolInventoried(Spool spool, BambuFilamentInfo info)
        {
            currentSpool = spool;
            currentInfo = info;
            RaiseSpoolFound(BuildSpoolFound(spool, info), info);
        }

        private static SpoolFound BuildSpoolFound(Spool spool, BambuFilamentInfo info) => new(
            Material: spool.Filament.Material,
            TrayUid: info.TrayUid,
            Weight: spool.SpoolWeight.GetValueOrDefault() + spool.InitialWeight.GetValueOrDefault() - spool.UsedWeight,
            EmptyWeight: spool.SpoolWeight,
            Price: spool.Price,
            BuyDate: spool.Extra.TryGetValue(ExtraBuyDate, out var buyDateOut) && DateTime.TryParse(buyDateOut.Replace("\"", ""), out var buyDate) ? buyDate : null,
            LotNr: spool.LotNr,
            Location: spool.Location);

        public override async Task UpdateCurrentSpoolAsync(SpoolEditInput input)
        {
            if (currentSpool == null) return;

            await UpdateSpool(
                currentSpool,
                input.BuyDate,
                input.Price,
                input.LotNr,
                input.Location,
                input.EmptyWeight.GetValueOrDefault(),
                currentSpool.InitialWeight.GetValueOrDefault(),
                input.Weight.GetValueOrDefault(),
                currentInfo?.TrayUid,
                currentInfo?.ProductionDateTime);
        }
    }
}
