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
    public class SpoolmanManager(ILogger<SpoolmanManager>? logger)
    {
        public delegate void StatusChangedEventHandler();
        public delegate void LocationsLoadedEventHandler();
        public delegate void ShowMessageEventHandler(bool isError, string message);
        public delegate void LogMessageEventHandler(LogLevel level, string message);
        public delegate void SpoolFoundEventHandler(Spool spool, BambuFillamentInfo info);
        public delegate void PlayErrorToneEventHandler();

        public event StatusChangedEventHandler? OnStatusChanged;
        public event LocationsLoadedEventHandler? OnLocationsLoaded;
        public event ShowMessageEventHandler? OnShowMessage;
        public event LogMessageEventHandler? OnLogMessage;
        public event SpoolFoundEventHandler? OnSpoolFound;
        public event PlayErrorToneEventHandler? OnPlayErrorTone;

        public const string DefaultBambuLabVendor = "Bambu Lab";
        public const string ExtraBuyDate = "buy_date";
        public const string ExtraTag = "tag";
        public const string ExtraProductionDateTime = "production_time";

        private IHost? apiHost;
        private string? initializedApiUrl;
        private bool isInitialized;
        private readonly object filamentLock = new();
        private List<ExternalFilament> bambuLabExternalFilaments = [];
        private Timer? healthCheckTimer;
        private bool healthCheckInProgress;

        public string? AppVersion { get; set; }

        public bool ShowLogs { get; set; }

        public string? ApiUrl { get; set; }

        public bool UnknownFilamentEnabled { get; set; } = false;

        public SpoolManDefaults Defaults { get; } = new();

        public bool IsHealth { get; set; }

        public bool IsInitialized => isInitialized;

        public SpoolmanManagerStatusType Status { get; private set; } = SpoolmanManagerStatusType.Initializing;

        public Vendor? BambuLabsVendor { get; set; }

        public string[] ExistingLocations { get; set; } = [];

        /// <summary>
        /// All external filaments
        /// </summary>
        public List<ExternalFilament> AllExternalFilaments { get; set; } = [];

        /// <summary>
        /// Bambu lab's external filaments (thread-safe; updated from background API fetch)
        /// </summary>
        public List<ExternalFilament> BambuLabExternalFilaments
        {
            get { lock (filamentLock) return bambuLabExternalFilaments; }
            set { lock (filamentLock) bambuLabExternalFilaments = value; }
        }

        /// <summary>
        /// Unknown filament, if no filament is found or multiple result where found, return this
        /// </summary>
        public ExternalFilament UnknownFilament { get; } = GenerateUnknownFilament();

        public async Task Init()
        {
            if (AppVersion != null) await Log(LogLevel.Information, $"App version {AppVersion}");

            if (string.IsNullOrEmpty(ApiUrl))
            {
                await LogAndSetStatus(SpoolmanManagerStatusType.ApiUrlMissing, LogLevel.Information, "Api url not set");
                return;
            }

            // Fast path: already initialized with the same URL — just verify health
            if (isInitialized && initializedApiUrl == ApiUrl)
            {
                try
                {
                    if (await CheckHealth())
                        await LogAndSetStatus(SpoolmanManagerStatusType.Ready, LogLevel.Success, "Ready to inventory fillament");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    await LogAndSetStatus(SpoolmanManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Health check failed: {ex.Message}");
                }
                return;
            }

            // Full initialization path
            await LogAndSetStatus(SpoolmanManagerStatusType.Initializing, LogLevel.Information, "Initializing ...");

            var apiUrl = ApiUrl.EndsWith("/") ? ApiUrl.Substring(0, ApiUrl.Length - 1) : ApiUrl;
            apiUrl = apiUrl.Contains("api/v1") ? apiUrl : $"{apiUrl}/api/v1";

            apiHost = Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
                {
                    services.AddApi(options =>
                    {
                        options.AddApiHttpClients(client =>
                        {
                            client.BaseAddress = new Uri(apiUrl);
                            client.Timeout = TimeSpan.FromSeconds(5);

                            if (!string.IsNullOrEmpty(client.BaseAddress.UserInfo)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client.BaseAddress.UserInfo)));

                        }, builder =>
                        {
                            builder
                                .AddRetryPolicy(3)
                                .AddCircuitBreakerPolicy(5, TimeSpan.FromSeconds(30));
                        });
                    });
                })
            .Build();

            try
            {
                // Health check with retry (up to 3 attempts, 3s delay between)
                var healthOk = false;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        healthOk = await CheckHealth();
                        if (healthOk) break;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        await Log(LogLevel.Warning, $"Health check attempt {attempt}/3 failed: {ex.Message}");
                    }

                    if (attempt < 3) await Task.Delay(3000);
                }

                if (!healthOk) return;

                await CheckDefaultValues();

                await LoadLocalFilaments();

                await LoadLocations();

                initializedApiUrl = ApiUrl;
                isInitialized = true;

                await LogAndSetStatus(SpoolmanManagerStatusType.Ready, LogLevel.Success, "Ready to inventory fillament");

                StartHealthCheckTimer();

                _ = LoadExternalFilamentsInBackground();
            }
            catch (HttpRequestException ex)
            {
                await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, ex.ToString());
            }
            catch (Exception e)
            {
                await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, e.ToString());
                logger?.LogError(e, "Error connecting to api");
            }
        }

        public SpoolmanManager Test()
        {
            ExtendWithMissingFilaments(BambuLabExternalFilaments).Wait();
            return this;
        }

        private async Task<bool> CheckHealth()
        {
            if (apiHost == null) return false;

            var defaultApi = apiHost.Services.GetRequiredService<IDefaultApi>();
            var health = await defaultApi.HealthHealthGetAsync();

            if (health.TryOk(out var check))
            {
                if (check.Status == "healthy")
                {
                    await LogAndSetStatus(SpoolmanManagerStatusType.ApiConnected, LogLevel.Success, $"Api connected, spoolman status: {check.Status}");
                    return IsHealth = true;
                }

                await LogAndSetStatus(SpoolmanManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Api connected and health check returned: {check.Status}");
            }
            else
            {
                await LogAndSetStatus(SpoolmanManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Can't connect to api. Api response: {health.RawContent}");
            }

            return IsHealth = false;
        }

        private async Task CheckDefaultValues()
        {
            if (apiHost == null) return;

            await LogAndSetStatus(SpoolmanManagerStatusType.CheckingDefaults, LogLevel.Information, "Checking default settings");

            #region Default vendor

            var vendorApi = apiHost.Services.GetRequiredService<IVendorApi>();

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
                        await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't add default '{DefaultBambuLabVendor}' vendor. Api response: {vendorAddResponse.RawContent}");
                        return;
                    }
                }
            }
            else
            {
                await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't add default '{DefaultBambuLabVendor}' vendor. Api response: {bambuLabsVendor.RawContent}");
                return;
            }

            #endregion

            #region Extra fields

            var fieldApi = apiHost.Services.GetRequiredService<IFieldApi>();

            var extraFields = new List<ExtraFieldModel>
            {
                new (true, EntityType.spool, 1, ExtraBuyDate, "Buy Date", ExtraFieldType.Datetime),
                new (false, EntityType.spool, 2, ExtraProductionDateTime, "Production Time", ExtraFieldType.Datetime),
                new (false, EntityType.spool, 3, "active_tray", "Active Tray", ExtraFieldType.Text),
                new (false, EntityType.spool, 4, ExtraTag, "Tag", ExtraFieldType.Text),

                new (false, EntityType.filament, 1, "type", "Type", ExtraFieldType.Choice) { Choices =  ["Silk", "Basic", "High Speed", "Matte", "Plus", "Flexible", "Translucent"], DefaultValue = "\"Basic\""},
                new (false, EntityType.filament, 2, "nozzle_temperature", "Nozzle Temperature", ExtraFieldType.IntegerRange) { Unit = "\u00b0C", DefaultValue = "[190,300]" }
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
                            await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't add extra field '{extraFieldModel.Key}'. Api response: {addFieldQuery.RawContent}");
                            return;
                        }
                    }

                    Defaults.ExtraFieldsAdded = true;
                }
                else
                {
                    await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't get existing extra fields. Api response: {existingFieldsQuery.RawContent}");
                    return;
                }
            }

            #endregion

            await LogAndSetStatus(SpoolmanManagerStatusType.DefaultsOk, LogLevel.Success, "Spoolman default setting ok");
        }

        private async Task LoadLocalFilaments()
        {
            var localFilaments = new List<ExternalFilament>();
            await ExtendWithMissingFilaments(localFilaments);
            BambuLabExternalFilaments = localFilaments;
            await Log(LogLevel.Information, $"Loaded local filaments: {localFilaments.Count}");
        }

        private async Task LoadExternalFilamentsInBackground()
        {
            try
            {
                if (apiHost == null) return;

                var externalApi = apiHost.Services.GetRequiredService<IExternalApi>();
                var allExternalFilaments = await externalApi.GetAllExternalFilamentsExternalFilamentGetAsync();

                if (allExternalFilaments.TryOk(out var list))
                {
                    AllExternalFilaments = list;

                    // Build merged list: API filaments as base, extend with local-only entries
                    var apiBambuFilaments = list.Where(x => x.Manufacturer == DefaultBambuLabVendor).ToList();
                    await ExtendWithMissingFilaments(apiBambuFilaments);

                    // Swap atomically
                    BambuLabExternalFilaments = apiBambuFilaments;

                    await Log(LogLevel.Information, $"Background: loaded external filaments: {AllExternalFilaments.Count}, merged Bambu Lab: {apiBambuFilaments.Count}");
                }
                else
                {
                    await Log(LogLevel.Warning, $"Background: error loading external filaments. Api response: {allExternalFilaments.RawContent}");
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

        private async Task LoadLocations()
        {
            if (apiHost == null) return;

            #region Load locations

            var settingApi = apiHost.Services.GetRequiredService<ISettingApi>();

            var locationsRequest = await settingApi.GetSettingSettingKeyGetOrDefaultAsync("locations");

            if (locationsRequest != null && locationsRequest.TryOk(out var locations))
            {
                ExistingLocations = JsonConvert.DeserializeObject<string[]>(locations.Value) ?? [];

                OnLocationsLoaded?.Invoke();
            }

            #endregion
        }

        public async Task<bool> InventorySpool(BambuFillamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            if (apiHost == null) return false;

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

                var spoolApi = apiHost.Services.GetRequiredService<ISpoolApi>();
                var spoolQuery = await spoolApi.FindSpoolSpoolGetAsync(filamentId2: new Option<string?>($"{filament.Id}"), allowArchived: new Option<bool>(true));

                if (spoolQuery.TryOk(out var spools))
                {
                    var spool = spools.FirstOrDefault(x => x.Extra.TryGetValue(ExtraTag, out var value) && value.Equals($"\"{info.TrayUid}\"", StringComparison.CurrentCultureIgnoreCase));

                    if (spool == null) await AddSpool(info, buyDate, price, lotNr, location, filament, spoolApi);
                    else
                    {
                        OnShowMessage?.Invoke(false, $"Existing spool '{info.TrayUid?.TrimTo(14, "...")}' fount");
                        await Log(LogLevel.Success, $"Existing spool '{info.TrayUid?.TrimTo(14, "...")}' fount");
                        OnSpoolFound?.Invoke(spool, info);
                    }
                }
                else
                {
                    await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't load existing spools. Api response: {spoolQuery.RawContent}");
                }

                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Inventory spool");
                return false;
            }
        }

        public async Task<ExternalFilament?> FindExternalFilament(BambuFillamentInfo info)
        {
            var result = await FindExternalFilament(BambuLabExternalFilaments, info);

            var spoolmanErrorLevel = UnknownFilamentEnabled ? SpoolmanManagerStatusType.Ready : SpoolmanManagerStatusType.Error;
            var logLevel = UnknownFilamentEnabled ? LogLevel.Warning : LogLevel.Error;

            switch (result.Count)
            {
                case > 1:
                    {
                        foreach (var item in result)
                        {
                            await Log(LogLevel.Debug, item.ToString());
                        }

                        await LogAndSetStatus(spoolmanErrorLevel, logLevel, "Found more then 1 matching filament", new Exception($"{JsonConvert.SerializeObject(info)}\r\n{string.Join("\t\n", result.Select(x => x.ToString()))}"));
                        OnPlayErrorTone?.Invoke();
                        return null;
                    }
                case 0:
                    await LogAndSetStatus(spoolmanErrorLevel, logLevel, "No matching filament found", new Exception($"{JsonConvert.SerializeObject(info)}"));
                    OnPlayErrorTone?.Invoke();
                    return null;
            }

            return result.First();
        }

        public static Task<List<ExternalFilament>> FindExternalFilament(List<ExternalFilament> externalFilaments, BambuFillamentInfo info)
        {
            var transparentFilaments = new[]
            {
                "bambulab_pc_clearblack_1000_175_n",
                "bambulab_pva_clear_500_175_n"
            };

            var hexColor = info.Color?.Substring(0, 6) ?? string.Empty;
            var opacity = info.Color?.Substring(6).StringToByteArray().FirstOrDefault() ?? 255;
            var transparent = opacity < 255;
            var color = hexColor;

            var query = externalFilaments.AsQueryable();

            query = query.Where(x => x.Material.EqualsCI(info.FilamentType) ||
                                     info.DetailedFilamentType.EqualsCI("PA6-GF") && x.Material.EqualsCI("PA6-GF") ||
                                     info.DetailedFilamentType.EqualsCI("ASA Aero") && x.Material.EqualsCI("ASA") && x.Name.ContainsCI("Aero") ||
                                     info.DetailedFilamentType.EqualsCI("PLA Aero") && x.Material.EqualsCI("PLA") && x.Name.ContainsCI("Aero") ||
                                     info.DetailedFilamentType.EqualsCI("PA-CF") && x.Material.EqualsCI("PA6-CF") ||
                                     info.DetailedFilamentType.EqualsCI("PAHT-CF") && x.Material.EqualsCI("PAHT-CF") ||
                                     info.DetailedFilamentType.EqualsCI("PLA Wood") && x.Material.EqualsCI("PLA+WOOD") ||
                                     info.DetailedFilamentType.EqualsCI("TPU for AMS") && x.Material.EqualsCI("TPU") && x.Name.StartsWithCI("For AMS"));

            //#if DEBUG
            //            var resultWitType = query.ToArray();
            //#endif

            query = query.Where(x => (x.ColorHex.EqualsCI(color)) ||
                                     (x.ColorHexes != null && color != null && x.ColorHexes.Contains(color, StringComparer.OrdinalIgnoreCase)) ||
                                     (info.DetailedFilamentType.EqualsCI("PLA Matte") && color.EqualsCI("E4BDD0") && x.ColorHex.EqualsCI("E8AFCF")) || //ASA filament hex color is different on spoolman db vs tag
                                     (info.FilamentType.EqualsCI("ASA") && color.EqualsCI("FFFFFF") && x.ColorHex.EqualsCI("FFFAF2")) || //ASA filament hex color is different on spoolman db vs tag
                                     (info.FilamentType.EqualsCI("ASA Aero") && color.EqualsCI("E9E4D9") && x.ColorHex.EqualsCI("F5F1DD")) || //ASA filament hex color is different on spoolman db vs tag
                                     (info.FilamentType.EqualsCI("PC") && color.EqualsCI("000000") && transparent && x.ColorHex.EqualsCI("5A5161")) || //PC Clear Black filament hex color is different on spoolman db vs tag
                                     (info.DetailedFilamentType.EqualsCI("PLA Wood") && color.EqualsCI("3F231C") && x.ColorHex.EqualsCI("4C241C")) || //PETG HF red filament hex color is different on spoolman db vs tag
                                     (info.DetailedFilamentType.EqualsCI("PETG HF") && color.EqualsCI("BC0900") && x.ColorHex.EqualsCI("EB3A3A")) || //PETG HF red filament hex color is different on spoolman db vs tag
                                     (info.DetailedFilamentType.EqualsCI("PETG Translucent") && color.EqualsCI("000000") && x.ColorHex.EqualsCI("FFFFFF")));  //PETG Translucent clear filament hex color is different on spoolman db vs tag

            //#if DEBUG
            //            var resultWitColor = query.ToArray();
            //#endif

            query = query.Where(x => (transparentFilaments.AsEnumerable().Contains(x.Id) && transparent) || x.Translucent == transparent || x.Translucent == null && !transparent);

            //#if DEBUG
            //            var resultWitTransparency = query.ToArray();
            //#endif

            if (info.DetailedFilamentType.ContainsCI("Support"))
            {
                var idToSearch = string.Empty;
                var nameToSearch = info.DetailedFilamentType;

                //white translucent Support for PLA is identified as black. Don't know if black is same 
                if (info.DetailedFilamentType.EqualsCI("Support for PLA") && info.MaterialVariantIdentifier.EqualsCI("S05-C0"))
                {
                    idToSearch = "bambulab_pla_supportforpla/petgnature_500_175_n";
                }

                //white translucent Support for PLA is identified as black. Don't know if black is same 
                if ((info.DetailedFilamentType.EqualsCI("Support W") && info.MaterialVariantIdentifier.EqualsCI("S00-W0")) ||
                    (info.DetailedFilamentType.EqualsCI("Support for PLA") && info.MaterialVariantIdentifier.EqualsCI("S02-W1")) ||
                    (info.DetailedFilamentType.EqualsCI("Support for PLA") && info.MaterialVariantIdentifier.EqualsCI("S02-W0")))
                {
                    idToSearch = "bambulab_pla_supportforplawhite_500_175_n";
                }

                //white translucent Support for PLA is identified as black. Don't know if black is same 
                if (info.DetailedFilamentType.EqualsCI("Support For PA") && info.MaterialVariantIdentifier.EqualsCI("S03-G1"))
                {
                    idToSearch = "bambulab_pa_supportforpa/pet_500_175_n";
                }

                if (idToSearch.IsNotNullOrEmpty()) query = externalFilaments.Where(x => x.Id.EqualsCI(idToSearch)).AsQueryable();
                else query = externalFilaments.Where(x => x.Name.StartsWithCI(nameToSearch)).Where(x => x.ColorHex.EqualsCI(hexColor)).AsQueryable();
            }
            else if (info.ColorCount.GetValueOrDefault() > 1 && query.Count() != 1) //multi color spool
            {
                var hexSecondColor = info.SecondColor?.Substring(0, 6) ?? string.Empty;
                var colors = new[] { color, hexSecondColor };

                if (info.MaterialVariantIdentifier.EqualsCI("A05-T1")) colors = ["FF9425", "FCA2BF"];
                if (info.MaterialVariantIdentifier.EqualsCI("A05-T2")) colors = ["0047BB", "7D1B49"];
                if (info.MaterialVariantIdentifier.EqualsCI("A05-T3")) colors = ["0047BB", "BB22A3"];
                if (info.MaterialVariantIdentifier.EqualsCI("A05-T4")) colors = ["60A4E8", "4CE4A0"];
                if (info.MaterialVariantIdentifier.EqualsCI("A05-T5")) colors = ["000000", "A34342"];
                if (info.MaterialVariantIdentifier.EqualsCI("A00-M5")) colors = ["6FCAEF", "8573DD"];
                if (info.MaterialVariantIdentifier.EqualsCI("A00-M6")) colors = ["ED9558", "CE4406"];

                query = externalFilaments
                    .Where(x => x.Material == info.FilamentType)
                    .Where(x => x.ColorHexes != null && colors.All(c => x.ColorHexes.Contains(c, StringComparer.OrdinalIgnoreCase))).AsQueryable();
            }
            else query = query.Where(x => !x.Name.ContainsCI("Support"));

            query = info.DetailedFilamentType switch
            {
                var type when type.EqualsCI("PETG Basic") => query.Where(x => x.Name.StartsWithCI("Basic ")),
                var type when type.EqualsCI("PETG HF") => query.Where(x => x.Name.StartsWithCI("HF ")),
                var type when type.EqualsCI("PC FR") => query.Where(x => x.Name.StartsWithCI("FR ")),

                var type when type.ContainsCI("Basic") => query.Where(x => x.Finish == null && x.Pattern == null && !x.Name.ContainsCI("Aero") && !x.Name.ContainsCI("Tough+")),
                var type when type.ContainsCI("Matte") => query.Where(x => x.Finish == Finish.Matte),
                var type when type.ContainsCI("Glow") => query.Where(x => x.Glow == true),
                var type when type.ContainsCI("Silk+") => query.Where(x => x.Name.ContainsCI("Silk+")),
                var type when type.ContainsCI("Tough+") => query.Where(x => x.Name.ContainsCI("Tough+")),
                var type when type.ContainsCI("Aero") => query.Where(x => x.Name.ContainsCI("Aero")),
                var type when type.ContainsCI("Sparkle") => query.Where(x => x.Name.ContainsCI("Sparkle")),
                var type when type.ContainsCI("Silk") ||
                              type.ContainsCI("Metallic") ||
                              type.ContainsCI("Galaxy") => query.Where(x => x.Finish == Finish.Glossy),

                _ => query
            };

            if (info.DetailedFilamentType.EqualsCI("PC"))
            {
                query = query.Where(x => !x.Name.StartsWithCI("FR "));

                if (color == "FFFFFF" && info.UniqueMaterialIdentifier.EqualsCI("FC00")) query = query.Where(x => x.Name.EqualsCI("White") || x.Name.EqualsCI("Weiß"));
                if (color == "FFFFFF" && !info.UniqueMaterialIdentifier.EqualsCI("FC00")) query = query.Where(x => x.Name.EqualsCI("Transparent"));
            }

            if (info.MaterialVariantIdentifier.EqualsCI("A00-W1") || info.MaterialVariantIdentifier.EqualsCI("A00-W01")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pla_jadewhite_1000_175_n")).AsQueryable();

            if (info.DetailedFilamentType.EqualsCI("PLA Basic") && color.EqualsCI("84754E")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pla_bronze_1000_175_n")).AsQueryable();

            var result = query.ToList();

            #region test if spool info is same only weight differs, select closest weight

            if (result.Count > 1)
            {
                var typeGroup = result.GroupBy(x =>
                {
                    var spoolType = x.SpoolType switch
                    {
                        SpoolType.Cardboard => "c",
                        SpoolType.Plastic => "p",
                        SpoolType.Metal => "m",
                        _ => "n"
                    };

                    return $"{x.Manufacturer}|{x.Material}|{x.Name}|{x.Diameter * 100:0}|{spoolType}";
                }).ToList();

                if (typeGroup.Count == 1)
                {
                    var bestMatchWeight = typeGroup.First().OrderByDescending(x => x.SpoolWeight).FirstOrDefault(x => x.SpoolWeight <= info.SpoolWeight) ??
                                          typeGroup.First().OrderBy(x => x.SpoolWeight).FirstOrDefault(x => x.SpoolWeight > info.SpoolWeight);

                    if (bestMatchWeight != null) result = [bestMatchWeight];
                }
            }

            #endregion

            return Task.FromResult(result);
        }

        private async Task<Filament?> AddOrUpdateFilament(ExternalFilament externalFilament, decimal? price, BambuFillamentInfo info)
        {
            if (apiHost == null) return null;

            try
            {
                var filamentApi = apiHost.Services.GetRequiredService<IFilamentApi>();

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

                    await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't add filament. Api response: {filamentAddResult.RawContent}");
                    return null;
                }

                await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't load existing filaments. Api response: {filamentQuery.RawContent}");
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Add/update filament");
                return null;
            }
        }

        private async Task AddSpool(BambuFillamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location, Filament filament, ISpoolApi spoolApi)
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
                    OnShowMessage?.Invoke(false, $"Spool '{info.TrayUid?.TrimTo(14, "...")}' added");
                    await Log(LogLevel.Success, $"Spool '{info.TrayUid?.TrimTo(14, "...")}' added");
                    OnSpoolFound?.Invoke(addedSpool, info);
                }
                else
                {
                    await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't add spool. Api response: {spoolAddResult.RawContent}");
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
            if (apiHost == null) return;

            try
            {
                var spoolApi = apiHost.Services.GetRequiredService<ISpoolApi>();

                trayUid ??= currentSpool.Extra.TryGetValue(ExtraTag, out var tagOut) ? tagOut.Replace("\"", "") : string.Empty;

                var usedWeight = Math.Max(emptyWeight + initialWeight - spoolWeight, 0);

                var extraValues = currentSpool.Extra;

                if (productionDateTime != null) extraValues[ExtraProductionDateTime] = $"\"{productionDateTime:yyyy-MM-ddZHH:mm:ss}\"";

                if (buyDate == null && extraValues.ContainsKey(ExtraBuyDate)) extraValues.Remove(ExtraBuyDate);
                else if (buyDate != null) extraValues[ExtraBuyDate] = $"\"{buyDate:yyyy-MM-dd}Z00:00:00\"";
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

                if (spoolUpdateResult.TryOk(out _))
                {
                    OnShowMessage?.Invoke(false, $"Spool '{trayUid}' updated, used weight set to {usedWeight:0.#}g");
                    await Log(LogLevel.Success, $"Spool '{trayUid}' updated, used weight set to {usedWeight:0.#}g");
                }
                else
                {
                    await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Can't update spool. Api response: {spoolUpdateResult.RawContent}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await HandleNetworkError(ex, "Update spool");
            }
        }

        #region Health check timer

        private void StartHealthCheckTimer()
        {
            healthCheckTimer?.Dispose();
            healthCheckTimer = new Timer(async void (_) =>
            {
                try
                {
                    await PerformHealthCheck();
                }
                catch (Exception)
                {
                    // ignored
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private async Task PerformHealthCheck()
        {
            if (healthCheckInProgress || apiHost == null) return;
            healthCheckInProgress = true;

            try
            {
                var wasHealthy = IsHealth;
                var defaultApi = apiHost.Services.GetRequiredService<IDefaultApi>();
                var health = await defaultApi.HealthHealthGetAsync();

                IsHealth = health.TryOk(out var check) && check.Status == "healthy";

                if (wasHealthy != IsHealth)
                    OnStatusChanged?.Invoke();
            }
            catch
            {
                if (IsHealth)
                {
                    IsHealth = false;
                    OnStatusChanged?.Invoke();
                }
            }
            finally
            {
                healthCheckInProgress = false;
            }
        }

        #endregion

        #region Error handling

        private async Task HandleNetworkError(Exception ex, string operation)
        {
            var message = ex is TaskCanceledException
                ? $"{operation} timed out. Check your network connection."
                : $"{operation} failed. Check your network connection.";

            OnShowMessage?.Invoke(true, message);
            await Log(LogLevel.Information, $"{operation}: {ex.Message}");
        }

        #endregion

        #region Logging and Status

        private async Task LogAndSetStatus(SpoolmanManagerStatusType status, LogLevel level, string message, Exception? exception = null)
        {
            await Log(level, message, exception);
            await SetStatus(status);
        }

        private Task SetStatus(SpoolmanManagerStatusType status)
        {
            Status = status;
            OnStatusChanged?.Invoke();

            return Task.CompletedTask;
        }

        private Task Log(LogLevel level, string message, Exception? ex = null)
        {
            if (ShowLogs) OnLogMessage?.Invoke(level, message);

            switch (level)
            {
                case LogLevel.Trace:
                    logger?.LogTrace(message);
                    break;
                case LogLevel.Debug:
                    logger?.LogDebug(message);
                    break;
                case LogLevel.Information:
                case LogLevel.Success:
                    logger?.LogInformation(message);
                    break;
                case LogLevel.Warning:
                    logger?.LogWarning(ex, message);
                    break;
                case LogLevel.Error:
                    logger?.LogError(ex, message);
                    break;
                case LogLevel.Critical:
                    logger?.LogCritical(ex, message);
                    break;
                case LogLevel.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Missing filaments

        private static ExternalFilament GenerateUnknownFilament()
        {
            const string name = "Unknown";
            const string material = "UNKNOWN";

            var id = FilamentIdGenerator.GenerateId(DefaultBambuLabVendor, name, material, 1000, 1.75m);

            return new ExternalFilament(
                id,
                DefaultBambuLabVendor,
                name,
                material,
                1.22m,
                1000,
                1.75m
            );
        }

        private Task ExtendWithMissingFilaments(List<ExternalFilament> externalFilaments)
        {
            var assembly = typeof(SpoolmanManager).Assembly;
            using var stream = assembly.GetManifestResourceStream("BambuMan.Shared.Resources.filaments.json");

            if (stream == null) throw new FileNotFoundException("Embedded resource not found");

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var fillamentInfos = JsonConvert.DeserializeObject<FilamentData[]>(json);
            if (fillamentInfos == null) return Task.CompletedTask;

            foreach (var fillamentInfo in fillamentInfos)
            {
                if (externalFilaments.Any(x => x.Id == fillamentInfo.Id && x.Weight == fillamentInfo.WeightValue)) continue;

                var filament = new ExternalFilament(
                    fillamentInfo.Id,
                    fillamentInfo.Manufacturer,
                    fillamentInfo.Name,
                    fillamentInfo.Material,
                    fillamentInfo.Density,
                    fillamentInfo.WeightValue,
                    fillamentInfo.Diameter,
                    spoolWeight: new Option<decimal?>(fillamentInfo.SpoolWeight),
                    spoolType: new Option<SpoolType?>(SpoolTypeValueConverter.FromStringOrDefault(fillamentInfo.SpoolType ?? "")),
                    colorHex: new Option<string?>(fillamentInfo.ColorHex),
                    colorHexes: new Option<List<string>?>(fillamentInfo.ColorHexes?.ToList()),
                    extruderTemp: new Option<int?>(fillamentInfo.ExtruderTemp),
                    bedTemp: new Option<int?>(fillamentInfo.BedTemp),
                    finish: new Option<Finish?>(FinishValueConverter.FromStringOrDefault(fillamentInfo.Finish ?? "")),
                    multiColorDirection: new Option<SpoolmanExternaldbMultiColorDirection?>(SpoolmanExternaldbMultiColorDirectionValueConverter.FromStringOrDefault(fillamentInfo.MultiColorDirection ?? "")), //not implemented jet
                    pattern: new Option<Pattern?>(PatternValueConverter.FromStringOrDefault(fillamentInfo.Pattern ?? "")),
                    translucent: new Option<bool?>(fillamentInfo.Translucent),
                    glow: new Option<bool?>(fillamentInfo.Glow)
                );

                externalFilaments.Add(filament);
            }

            return Task.CompletedTask;
        }

        #endregion
    }
}
