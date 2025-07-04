﻿using System.Diagnostics;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Microsoft.Extensions.Hosting;
using SpoolMan.Api.Api;
using SpoolMan.Api.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpoolMan.Api.Client;
using SpoolMan.Api.Model;
using LogLevel = BambuMan.Shared.Enums.LogLevel;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace BambuMan.Shared
{
    public class SpoolmanManager(ILogger<SpoolmanManager>? logger)
    {
        public delegate void StatusChangedEventHandler();
        public delegate void ShowMessageEventHandler(bool isError, string message);
        public delegate void LogMessageEventHandler(LogLevel level, string message);
        public delegate void SpoolFoundEventHandler(Spool spool, BambuFillamentInfo info);
        public delegate void PlayErrorToneEventHandler();

        public event StatusChangedEventHandler? OnStatusChanged;
        public event ShowMessageEventHandler? OnShowMessage;
        public event LogMessageEventHandler? OnLogMessage;
        public event SpoolFoundEventHandler? OnSpoolFound;
        public event PlayErrorToneEventHandler? OnPlayErrorTone;

        public const string DefaultBambuLabVendor = "Bambu Lab";
        public const string ExtraBuyDate = "buy_date";
        public const string ExtraTag = "tag";
        public const string ExtraProductionDateTime = "production_time";

        private IHost? apiHost;

        public string? AppVersion { get; set; }

        public bool ShowLogs { get; set; }

        public string? ApiUrl { get; set; }

        public SpoolManDefaults Defaults { get; } = new();

        public bool IsHealth { get; set; }

        public SpoolmanManagerStatusType Status { get; private set; } = SpoolmanManagerStatusType.Initializing;

        public Vendor? BambuLabsVendor { get; set; }

        /// <summary>
        /// All external filaments
        /// </summary>
        public List<ExternalFilament> AllExternalFilaments { get; set; } = new();

        /// <summary>
        /// Bambu lab's external filaments
        /// </summary>
        public List<ExternalFilament> BambuLabExternalFilaments { get; set; } = new();

        public async Task Init()
        {
            if (AppVersion != null) await Log(LogLevel.Information, $"App version {AppVersion}");

            await LogAndSetStatus(SpoolmanManagerStatusType.Initializing, LogLevel.Information, "Initializing ...");

            if (string.IsNullOrEmpty(ApiUrl))
            {
                await LogAndSetStatus(SpoolmanManagerStatusType.ApiUrlMissing, LogLevel.Information, "Api url no set");
                return;
            }

            var apiUrl = ApiUrl.EndsWith("/") ? ApiUrl.Substring(0, ApiUrl.Length - 1) : ApiUrl;
            apiUrl = apiUrl.Contains("api/v1") ? apiUrl : $"{apiUrl}/api/v1";

            apiHost = Host.CreateDefaultBuilder([]).ConfigureServices((_, services) =>
                {
                    services.AddApi(options =>
                    {
                        options.AddApiHttpClients(client =>
                        {
                            client.BaseAddress = new Uri(apiUrl);

                            if (!string.IsNullOrEmpty(client.BaseAddress.UserInfo))
                            {
                                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client.BaseAddress.UserInfo)));
                            }
                        });
                    });
                })
            .Build();

            try
            {
                if (!await CheckHealth()) return;

                await Task.Delay(500);

                await CheckDefaultValues();
                await LoadAllExternalFilaments();

                await Task.Delay(500);

                await LogAndSetStatus(SpoolmanManagerStatusType.Ready, LogLevel.Success, "Ready to inventory fillament");

            }
            catch (HttpRequestException ex)
            {
                OnLogMessage?.Invoke(LogLevel.Error, ex.ToString());
            }
            catch (Exception e)
            {
                OnLogMessage?.Invoke(LogLevel.Error, e.ToString());
                logger?.LogError(e, "Error connecting to api");
            }
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

        private async Task LoadAllExternalFilaments()
        {
            if (apiHost == null) return;

            #region Load all external fillament

            var externalApi = apiHost.Services.GetRequiredService<IExternalApi>();

            var allExternalFilaments = await externalApi.GetAllExternalFilamentsExternalFilamentGetAsync();

            if (allExternalFilaments.TryOk(out var list))
            {
                AllExternalFilaments = list;
                BambuLabExternalFilaments = list.Where(x => x.Manufacturer == DefaultBambuLabVendor).ToList();

                await LogAndSetStatus(SpoolmanManagerStatusType.AllExternalFilamentsLoaded, LogLevel.Information, $"Loaded all external filaments: {AllExternalFilaments.Count}");
                await Log(LogLevel.Information, $"Found '{DefaultBambuLabVendor}' filaments: {BambuLabExternalFilaments.Count}");
            }
            else
            {
                await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, $"Error loading all external filaments. Api response: {allExternalFilaments.RawContent}");
            }

            #endregion
        }

        public async Task InventorySpool(BambuFillamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            if (apiHost == null) return;

            var externalFilament = await FindExternalFilament(info);
            if (externalFilament == null) return;

            await Log(LogLevel.Debug, externalFilament.ToString());

            var filament = await AddOrUpdateFilament(externalFilament, price, info);
            if (filament == null) return;

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
        }

        private async Task<ExternalFilament?> FindExternalFilament(BambuFillamentInfo info)
        {
            var hexColor = info.Color?.Substring(0, 6) ?? string.Empty;
            var opacity = info.Color?.Substring(6).StringToByteArray().FirstOrDefault() ?? 255;
            var transparent = opacity < 255;

            var color = hexColor;

#if DEBUG
            // ReSharper disable once UnusedVariable
            var t = BambuLabExternalFilaments.FirstOrDefault(x => x.Id == "bambulab_petg_red_1000_175_n");
            // ReSharper disable once UnusedVariable
            var material = BambuLabExternalFilaments.Where(x => x.Material == info.FilamentType).ToArray();
#endif
            //6e88bc

            var query = BambuLabExternalFilaments
                .Where(x => x.Material == info.FilamentType ||
                            info.DetailedFilamentType == "PA-CF" && x.Material == "PA6-CF" ||
                            info.DetailedFilamentType == "PAHT-CF" && x.Material == "PAHT-CF" ||
                            info.DetailedFilamentType == "PLA Wood" && x.Material == "PLA+WOOD" ||
                            info.DetailedFilamentType == "TPU for AMS" && x.Material == "TPU" && x.Name.StartsWith("For AMS"))
                .Where(x => (x.ColorHex?.Equals(color, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                            (info.FilamentType == "ASA" && color == "FFFFFF" && (x.ColorHex?.Equals("FFFAF2", StringComparison.CurrentCultureIgnoreCase) ?? false)) || //ASA filament hex color is different on spoolman db vs tag
                            (info.DetailedFilamentType == "PETG HF" && color == "BC0900" && (x.ColorHex?.Equals("EB3A3A", StringComparison.CurrentCultureIgnoreCase) ?? false)) ||  //PETG HF red filament hex color is different on spoolman db vs tag
                            (info.DetailedFilamentType == "PETG Translucent" && color == "000000" && (x.ColorHex?.Equals("FFFFFF", StringComparison.CurrentCultureIgnoreCase) ?? false)))  //PETG Translucent clear filament hex color is different on spoolman db vs tag
                .Where(x => x.Translucent == transparent || x.Translucent == null && !transparent);

            if (info.DetailedFilamentType?.Contains("Support", StringComparison.CurrentCultureIgnoreCase) ?? false)
            {
                var nameToSearch = info.DetailedFilamentType;

                //white translucent Support for PLA is identified as black. Don't know if black is same 
                if (info is { DetailedFilamentType: "Support for PLA", MaterialVariantIdentifier: "S05-C0" })
                {
                    nameToSearch = "Support for PLA/PETG Nature";
                    hexColor = "FFFFFF";
                }
                
                //white translucent Support for PLA is identified as black. Don't know if black is same 
                if (info is { DetailedFilamentType: "Support W", MaterialVariantIdentifier: "S00-W0" })
                {
                    nameToSearch = "Support for PLA White";
                    hexColor = "FFFFFF";
                }

                query = BambuLabExternalFilaments
                    .Where(x => x.Name.StartsWith(nameToSearch, StringComparison.CurrentCultureIgnoreCase))
                    .Where(x => x.ColorHex?.Equals(hexColor, StringComparison.CurrentCultureIgnoreCase) ?? false);
            }
            //multi color spool
            else if (info.ColorCount.GetValueOrDefault() > 1)
            {
                var hexSecondColor = info.SecondColor?.Substring(0, 6) ?? string.Empty;
                var colors = new[] { color, hexSecondColor };

                query = BambuLabExternalFilaments
                    .Where(x => x.Material == info.FilamentType)
                    .Where(x => x.ColorHexes != null && colors.All(c => x.ColorHexes.Contains(c)));
            }
            else query = query.Where(x => !x.Name.Contains("Support", StringComparison.CurrentCultureIgnoreCase));

            if (info.DetailedFilamentType?.Contains("Basic", StringComparison.CurrentCultureIgnoreCase) ?? false) query = query.Where(x => x.Finish == null && x.Pattern == null);
            else if (info.DetailedFilamentType?.Contains("Matte", StringComparison.CurrentCultureIgnoreCase) ?? false) query = query.Where(x => x.Finish == Finish.Matte);
            else if (info.DetailedFilamentType?.Contains("Glow", StringComparison.CurrentCultureIgnoreCase) ?? false) query = query.Where(x => x.Glow == true);
            else if (info.DetailedFilamentType?.Contains("Silk", StringComparison.CurrentCultureIgnoreCase) ??
                     info.DetailedFilamentType?.Contains("Metallic", StringComparison.CurrentCultureIgnoreCase) ??
                     info.DetailedFilamentType?.Contains("Galaxy", StringComparison.CurrentCultureIgnoreCase) ??
                     false) query = query.Where(x => x.Finish == Finish.Glossy);

            if (info.DetailedFilamentType?.Equals("PETG HF", StringComparison.CurrentCultureIgnoreCase) ?? false)
                query = query.Where(x => x.Name.StartsWith("HF "));

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
            
            switch (result.Count)
            {
                case > 1:
                    {
                        foreach (var item in result)
                        {
                            await Log(LogLevel.Debug, item.ToString());
                        }

                        await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, "Found more then 1 matching filament", new Exception($"{JsonConvert.SerializeObject(info)}\r\n{string.Join("\t\n", result.Select(x => x.ToString()))}"));
                        OnPlayErrorTone?.Invoke();
                        return null;
                    }
                case 0:
                    await LogAndSetStatus(SpoolmanManagerStatusType.Error, LogLevel.Error, "No matching filament found", new Exception($"{JsonConvert.SerializeObject(info)}"));
                    OnPlayErrorTone?.Invoke();
                    return null;
            }

            return result.First();
        }

        private async Task<Filament?> AddOrUpdateFilament(ExternalFilament externalFilament, decimal? price, BambuFillamentInfo info)
        {
            if (apiHost == null) return null;

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

        private async Task AddSpool(BambuFillamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location, Filament filament, ISpoolApi spoolApi)
        {
            var extraValues = new Dictionary<string, string>
            {
                [ExtraProductionDateTime] = $"\"{info.ProductionDateTime:yyyy-MM-ddZHH:mm:ss}\"",
                [ExtraTag] = $"\"{info.TrayUid}\""
            };

            if (buyDate != null) extraValues[ExtraBuyDate] = $"\"{buyDate:yyyy-MM-dd}Z00:00:00\"";

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
                //comment: new Option<string?>(externalFilament.Comment),
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

        public async Task UpdateSpool(Spool currentSpool, DateTime? buyDate, decimal? price, string? lotNr, string? location,
            decimal emptyWeight, decimal initialWeight, decimal spoolWeight, string? trayUid = null, DateTime? productionDateTime = null)
        {
            if (apiHost == null) return;
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
    }
}
