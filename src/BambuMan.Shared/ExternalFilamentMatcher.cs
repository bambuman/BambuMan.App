using BambuMan.Shared.Models;
using Newtonsoft.Json;
using SpoolMan.Api.Client;
using SpoolMan.Api.Model;

namespace BambuMan.Shared
{
    /// <summary>
    /// Backend-neutral matching of a scanned Bambu Lab tag (<see cref="BambuFilamentInfo"/>) against the
    /// external filament catalog. Shared by SpoolMan and Bambuddy managers. Pure — no API / instance state.
    /// </summary>
    public static class ExternalFilamentMatcher
    {
        private const string DefaultBambuLabVendor = "Bambu Lab";

        /// <summary>The fallback filament returned when nothing matches and unknown filaments are enabled.</summary>
        public static ExternalFilament GenerateUnknownFilament()
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

        /// <summary>Load the embedded Bambu Lab catalog (<c>Resources/filaments.json</c>) into a fresh list.</summary>
        public static List<ExternalFilament> LoadEmbeddedFilaments()
        {
            var list = new List<ExternalFilament>();
            ExtendWithMissingFilaments(list);
            return list;
        }

        /// <summary>Merge the embedded catalog into <paramref name="externalFilaments"/>, skipping entries already present (by id + weight).</summary>
        public static void ExtendWithMissingFilaments(List<ExternalFilament> externalFilaments)
        {
            var assembly = typeof(ExternalFilamentMatcher).Assembly;
            using var stream = assembly.GetManifestResourceStream("BambuMan.Shared.Resources.filaments.json");

            if (stream == null) throw new FileNotFoundException("Embedded resource not found");

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var fillamentInfos = JsonConvert.DeserializeObject<FilamentData[]>(json);
            if (fillamentInfos == null) return;

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
        }

        /// <summary>
        /// Match a scanned tag against the supplied catalog. Returns all candidates — callers decide what 0 / 1 / &gt;1 means.
        /// </summary>
        public static Task<List<ExternalFilament>> FindExternalFilament(List<ExternalFilament> externalFilaments, BambuFilamentInfo info)
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

            query = query.Where(x => (x.ColorHex.EqualsCI(color)) ||
                                     (x.ColorHexes != null && color != null && x.ColorHexes.Contains(color, StringComparer.OrdinalIgnoreCase)) ||
                                     (info.DetailedFilamentType.EqualsCI("PLA Matte") && color.EqualsCI("E4BDD0") && x.ColorHex.EqualsCI("E8AFCF")) || //ASA filament hex color is different on spoolman db vs tag
                                     (info.FilamentType.EqualsCI("ASA") && color.EqualsCI("FFFFFF") && x.ColorHex.EqualsCI("FFFAF2")) || //ASA filament hex color is different on spoolman db vs tag
                                     (info.FilamentType.EqualsCI("ABS") && color.EqualsCI("ffb81c") && x.ColorHex.EqualsCI("FCE900")) || //ABS filament hex color is different on spoolman db vs tag
                                     (info.FilamentType.EqualsCI("ASA Aero") && color.EqualsCI("E9E4D9") && x.ColorHex.EqualsCI("F5F1DD")) || //ASA filament hex color is different on spoolman db vs tag
                                     (info.FilamentType.EqualsCI("PC") && color.EqualsCI("000000") && transparent && x.ColorHex.EqualsCI("5A5161")) || //PC Clear Black filament hex color is different on spoolman db vs tag
                                     (info.DetailedFilamentType.EqualsCI("PLA Wood") && color.EqualsCI("3F231C") && x.ColorHex.EqualsCI("4C241C")) || //PETG HF red filament hex color is different on spoolman db vs tag
                                     (info.DetailedFilamentType.EqualsCI("PETG HF") && color.EqualsCI("BC0900") && x.ColorHex.EqualsCI("EB3A3A")) || //PETG HF red filament hex color is different on spoolman db vs tag
                                     (info.DetailedFilamentType.EqualsCI("PETG Translucent") && color.EqualsCI("000000") && x.ColorHex.EqualsCI("FFFFFF")));  //PETG Translucent clear filament hex color is different on spoolman db vs tag

            query = query.Where(x => (transparentFilaments.AsEnumerable().Contains(x.Id) && transparent) || x.Translucent == transparent || x.Translucent == null && !transparent);

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

                query = idToSearch.IsNotNullOrEmpty() ?
                    externalFilaments.Where(x => x.Id.EqualsCI(idToSearch)).AsQueryable() :
                    externalFilaments.Where(x => x.Name.StartsWithCI(nameToSearch)).Where(x => x.ColorHex.EqualsCI(hexColor)).AsQueryable();
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

                var type when type.ContainsCI("Basic") => query.Where(x => x.Finish == null && x.Pattern == null && !x.Name.ContainsCI("Aero") && !x.Name.ContainsCI("Tough") && !x.Name.ContainsCI("Lite")),
                var type when type.ContainsCI("Matte") => query.Where(x => x.Finish == Finish.Matte),
                var type when type.ContainsCI("Glow") => query.Where(x => x.Glow == true),
                var type when type.ContainsCI("Silk+") => query.Where(x => x.Name.ContainsCI("Silk+")),
                var type when type.ContainsCI("Tough+") => query.Where(x => x.Name.ContainsCI("Tough+")),
                var type when type.ContainsCI("Aero") => query.Where(x => x.Name.ContainsCI("Aero")),
                var type when type.ContainsCI("Sparkle") => query.Where(x => x.Name.ContainsCI("Sparkle")),
                var type when type.ContainsCI("Lite") => query.Where(x => x.Name.ContainsCI("Lite")),
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
                if (info.Color == "68686580") query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pc_clearblack_1000_175_n")).AsQueryable();
            }

            if (info.MaterialVariantIdentifier.EqualsCI("A00-W1") || info.MaterialVariantIdentifier.EqualsCI("A00-W01")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pla_jadewhite_1000_175_n")).AsQueryable();
            if (info.MaterialVariantIdentifier.EqualsCI("S01-G1")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pa_supportforpa/pet_500_175_n")).AsQueryable();
            if (info.MaterialVariantIdentifier.EqualsCI("S04-Y0")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pva_clear_500_175_n")).AsQueryable();
            if (info.MaterialVariantIdentifier.EqualsCI("A00-Y00")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pla_yellow_1000_175_n")).AsQueryable();
            if (info.MaterialVariantIdentifier.EqualsCI("A00-B1")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pla_bluegray_1000_175_n")).AsQueryable();
            if (info.MaterialVariantIdentifier.EqualsCI("G00-B00")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_petg_basicreflexblue_1000_175_n")).AsQueryable();
            if (info.MaterialVariantIdentifier.EqualsCI("G00-B0")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_petg_blue_1000_175_n")).AsQueryable();
            if (info.MaterialVariantIdentifier.EqualsCI("A07-R5")) query = externalFilaments.Where(x => x.Id.EqualsCI("bambulab_pla_redgranite_1000_175_n")).AsQueryable();

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
    }
}
