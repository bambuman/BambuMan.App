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
        /// Does a scanned tag satisfy an override's criteria? Every supplied field must match; null fields are
        /// ignored. <paramref name="color"/> is the 6-character hex prefix — the criteria may also target the
        /// full 8-character <see cref="BambuFilamentInfo.Color"/> (hex + opacity).
        /// </summary>
        private static bool Matches(FilamentMatchCriteria? criteria, BambuFilamentInfo info, string color, bool transparent)
        {
            if (criteria == null) return false;

            if (criteria.MaterialVariantIdentifier != null && !info.MaterialVariantIdentifier.EqualsCI(criteria.MaterialVariantIdentifier)) return false;
            if (criteria.UniqueMaterialIdentifier != null && !info.UniqueMaterialIdentifier.EqualsCI(criteria.UniqueMaterialIdentifier)) return false;
            if (criteria.FilamentType != null && !info.FilamentType.EqualsCI(criteria.FilamentType)) return false;
            if (criteria.DetailedFilamentType != null && !info.DetailedFilamentType.EqualsCI(criteria.DetailedFilamentType)) return false;
            if (criteria.Color != null && !color.EqualsCI(criteria.Color) && !info.Color.EqualsCI(criteria.Color)) return false;
            if (criteria.Transparent != null && criteria.Transparent != transparent) return false;

            return true;
        }

        /// <summary>
        /// Match a scanned tag against the supplied catalog. Returns all candidates — callers decide what 0 / 1 / &gt;1 means.
        /// </summary>
        /// <param name="overrides">
        /// Per-SKU exceptions to apply. Defaults to <see cref="FilamentMatchOverrides.Internal"/>; callers that
        /// can reach the BambuMan API pass the possibly-newer set they fetched instead.
        /// </param>
        public static Task<List<ExternalFilament>> FindExternalFilament(List<ExternalFilament> externalFilaments, BambuFilamentInfo info, FilamentOverrideSet? overrides = null)
        {
            overrides ??= FilamentMatchOverrides.Internal;

            var transparentFilaments = overrides.TransparentFilamentIds;

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

            // Catalog colours accepted for this tag on top of its own colour, for the SKUs where the
            // spoolman external db and the Bambu tag disagree.
            var acceptedColorHexes = overrides.ColorHexes.Where(x => Matches(x.Criteria, info, color, transparent)).Select(x => x.CatalogColorHex).ToArray();

            query = query.Where(x => x.ColorHex.EqualsCI(color) ||
                                     (x.ColorHexes != null && x.ColorHexes.Contains(color, StringComparer.OrdinalIgnoreCase)) ||
                                     acceptedColorHexes.Any(c => x.ColorHex.EqualsCI(c)));

            //ids in TransparentFilamentIds are translucent even though the catalog says otherwise, so they
            //replace the catalog flag instead of adding to it — they must not also match an opaque tag.
            query = query.Where(x => transparentFilaments.AsEnumerable().Contains(x.Id) ?
                transparent :
                x.Translucent == transparent || x.Translucent == null && !transparent);

            if (info.DetailedFilamentType.ContainsCI("Support"))
            {
                var idToSearch = overrides.SupportForcedIds.FirstOrDefault(x => Matches(x.Criteria, info, color, transparent))?.FilamentId;
                var nameToSearch = info.DetailedFilamentType;

                query = idToSearch != null ?
                    externalFilaments.Where(x => x.Id.EqualsCI(idToSearch)).AsQueryable() :
                    externalFilaments.Where(x => x.Name.StartsWithCI(nameToSearch)).Where(x => x.ColorHex.EqualsCI(hexColor)).AsQueryable();
            }
            else if (info.ColorCount.GetValueOrDefault() > 1 && query.Count() != 1) //multi color spool
            {
                var hexSecondColor = info.SecondColor?.Substring(0, 6) ?? string.Empty;
                var colors = overrides.MultiColors.FirstOrDefault(x => Matches(x.Criteria, info, color, transparent))?.Colors ?? [color, hexSecondColor];

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

                var type when type.ContainsCI("Basic") => query.Where(x => x.Finish == null && x.Pattern == null && !x.Name.ContainsCI("Aero") && !x.Name.ContainsCI("Tough") && !x.Name.ContainsCI("Lite") && !x.Name.ContainsCI("Pure")),
                var type when type.ContainsCI("Matte") => query.Where(x => x.Finish == Finish.Matte),
                var type when type.ContainsCI("Glow") => query.Where(x => x.Glow == true),
                var type when type.ContainsCI("Silk+") => query.Where(x => x.Name.ContainsCI("Silk+")),
                var type when type.ContainsCI("Pure") => query.Where(x => x.Name.ContainsCI("Pure")),
                var type when type.ContainsCI("Tough+") => query.Where(x => x.Name.ContainsCI("Tough+")),
                var type when type.ContainsCI("Aero") => query.Where(x => x.Name.ContainsCI("Aero")),
                var type when type.ContainsCI("Sparkle") => query.Where(x => x.Name.ContainsCI("Sparkle")),
                var type when type.ContainsCI("Lite") => query.Where(x => x.Name.ContainsCI("Lite")),
                var type when type.ContainsCI("Silk") ||
                              type.ContainsCI("Metallic") ||
                              type.ContainsCI("Galaxy") => query.Where(x => x.Finish == Finish.Glossy),

                _ => query
            };

            if (info.DetailedFilamentType.EqualsCI("PC")) query = query.Where(x => !x.Name.StartsWithCI("FR "));

            //e.g. PC white vs transparent, which the catalog only distinguishes by name. First match wins.
            var nameFilter = overrides.NameFilters.FirstOrDefault(x => Matches(x.Criteria, info, color, transparent));
            if (nameFilter != null) query = query.Where(x => nameFilter.Names.Any(n => x.Name.EqualsCI(n)));

            //Last match wins, so a more specific entry added later supersedes an earlier one.
            var forcedId = overrides.ForcedIds.LastOrDefault(x => Matches(x.Criteria, info, color, transparent))?.FilamentId;
            if (forcedId != null) query = externalFilaments.Where(x => x.Id.EqualsCI(forcedId)).AsQueryable();

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
