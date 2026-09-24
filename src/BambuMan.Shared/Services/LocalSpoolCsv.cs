using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace BambuMan.Shared.Services
{
    /// <summary>Spools read from a csv, plus one message per row that couldn't be used.</summary>
    public record LocalSpoolCsvReadResult(List<LocalSpool> Spools, List<string> Errors);

    /// <summary>
    /// CSV export/import of the no-backend mode's spools.
    /// <para>
    /// The file leads with Bambuddy's spool csv columns, in Bambuddy's order, so it imports straight into Bambuddy's
    /// inventory (Inventory → Import CSV). Then come the tag fields Bambuddy's spool model has but its csv import
    /// doesn't read yet (<c>tag_uid</c>, <c>tray_uuid</c>, ...), and last BambuMan's own decoded tag fields.
    /// Bambuddy skips columns it doesn't know with a warning, so both extra groups are safe to carry, and together
    /// they let BambuMan read its own export back without losing anything.
    /// </para>
    /// </summary>
    public static class LocalSpoolCsv
    {
        /// <summary>Bambuddy's <c>CSV_COLUMNS</c> (backend/app/services/spool_csv.py), in its order.</summary>
        public static readonly string[] BambuddyColumns =
        [
            "material", "brand", "subtype", "color_name", "rgba", "extra_colors", "effect_type", "label_weight",
            "weight_used", "remaining", "cost_per_kg", "nozzle_temp_min", "nozzle_temp_max", "last_used", "note",
            "storage_location", "category", "low_stock_threshold_pct"
        ];

        /// <summary>Fields of Bambuddy's spool model that its csv import doesn't read (yet). Named as Bambuddy names them.</summary>
        public static readonly string[] TagColumns =
        [
            "tag_uid", "tray_uuid", "slicer_filament", "slicer_filament_name", "core_weight", "data_origin", "tag_type"
        ];

        /// <summary>BambuMan's own columns: the rest of the decoded tag, the catalog match and when it was scanned.</summary>
        public static readonly string[] BambuManColumns =
        [
            "manufacturer", "filament_name", "filament_code", "variant_id", "material_id", "filament_type",
            "detailed_filament_type", "diameter", "spool_width", "filament_length", "drying_temperature", "drying_time",
            "bed_temperature", "nozzle_diameter", "production_date", "color_count", "second_color", "price",
            "first_scanned", "last_scanned"
        ];

        public static readonly string[] Columns = [.. BambuddyColumns, .. TagColumns, .. BambuManColumns];

        /// <summary>Bambuddy's <c>ALLOWED_EFFECT_TYPES</c>. Anything else is rejected by its import, so it's left out.</summary>
        private static readonly HashSet<string> BambuddyEffectTypes =
        [
            "sparkle", "wood", "marble", "glow", "matte", "silk", "galaxy", "rainbow", "metal", "translucent",
            "gradient", "dual-color", "tri-color", "multicolor"
        ];

        /// <summary>
        /// A cell starting with one of these is run as a formula by Excel / Sheets / LibreOffice. Same guard, and the
        /// same leading-quote escape, as Bambuddy uses — so each side reads the other's files back unchanged.
        /// </summary>
        private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r'];

        private const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

        /// <summary>A date-stamped name for an export, so repeat exports don't overwrite each other.</summary>
        public static string FileName(DateTime now) => $"bambuman_inventory_{now:yyyyMMdd_HHmm}.csv";

        #region Export

        /// <summary>Write <paramref name="spools"/> as UTF-8 csv (with a BOM, so Excel picks the right encoding).</summary>
        public static void Write(Stream stream, IEnumerable<LocalSpool> spools)
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
            Write(writer, spools);
        }

        public static void Write(TextWriter writer, IEnumerable<LocalSpool> spools)
        {
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

            foreach (var column in Columns) csv.WriteField(column);
            csv.NextRecord();

            foreach (var spool in spools)
            {
                var row = ToRow(spool);

                foreach (var column in Columns) csv.WriteField(Sanitize(row.GetValueOrDefault(column)));
                csv.NextRecord();
            }

            csv.Flush();
        }

        private static Dictionary<string, string?> ToRow(LocalSpool spool)
        {
            var info = spool.Info;
            var (material, subtype, slicerFilament, slicerFilamentName) = BambuddyManager.DeriveFilament(info);
            var labelWeight = BambuddyManager.LabelWeight(info);
            var rgba = NormalizeHex(info.Color);

            return new Dictionary<string, string?>
            {
                // Same values a direct scan into Bambuddy produces — see BambuddyManager.BuildSpoolCreate.
                ["material"] = material,
                ["brand"] = "Bambu",
                ["subtype"] = subtype,
                ["color_name"] = spool.Name ?? info.Color,
                ["rgba"] = rgba,
                ["extra_colors"] = ExtraColors(spool),
                ["effect_type"] = EffectType(subtype),
                ["label_weight"] = Format(labelWeight),
                ["weight_used"] = "0",
                ["remaining"] = Format(labelWeight),
                ["cost_per_kg"] = Format(BambuddyManager.CostPerKg(spool.Price, labelWeight) is { } costPerKg ? Math.Round(costPerKg, 2) : null),
                ["nozzle_temp_min"] = Format(info.MinTemperatureForHotend),
                ["nozzle_temp_max"] = Format(info.MaxTemperatureForHotend),
                ["storage_location"] = spool.Location,

                ["tag_uid"] = info.SerialNumber,
                ["tray_uuid"] = info.TrayUid,
                ["slicer_filament"] = slicerFilament,
                ["slicer_filament_name"] = slicerFilamentName,
                ["core_weight"] = Format(BambuddyManager.CoreWeight),
                ["data_origin"] = "nfc_scan",
                ["tag_type"] = "bambu_rfid",

                ["manufacturer"] = spool.Manufacturer,
                ["filament_name"] = spool.Name,
                ["filament_code"] = spool.FilamentCode,
                ["variant_id"] = info.MaterialVariantIdentifier,
                ["material_id"] = info.UniqueMaterialIdentifier,
                ["filament_type"] = info.FilamentType,
                ["detailed_filament_type"] = info.DetailedFilamentType,
                ["diameter"] = Format(info.FilamentDiameter),
                ["spool_width"] = Format(info.SpoolWidth),
                ["filament_length"] = Format(info.FilamentLength),
                ["drying_temperature"] = Format(info.DryingTemperature),
                ["drying_time"] = Format(info.DryingTime),
                ["bed_temperature"] = Format(info.BedTemperature),
                ["nozzle_diameter"] = Format(info.NozzleDiameter),
                ["production_date"] = info.ProductionDateTime?.ToString(DateFormat, CultureInfo.InvariantCulture),
                ["color_count"] = Format(info.ColorCount),
                ["second_color"] = NormalizeHex(info.SecondColor),
                ["price"] = Format(spool.Price),
                ["first_scanned"] = spool.FirstScanned.ToString(DateFormat, CultureInfo.InvariantCulture),
                ["last_scanned"] = spool.LastScanned.ToString(DateFormat, CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Every colour stop of a multi-colour spool, comma separated — the catalog's list when it has one (the only
        /// source that can hold more than two stops), else the tag's two colours. Empty for a plain spool.
        /// </summary>
        private static string? ExtraColors(LocalSpool spool)
        {
            if (spool.ColorHexes.Count > 1) return string.Join(",", spool.ColorHexes.Select(NormalizeHex).OfType<string>());

            var info = spool.Info;

            if (info.ColorCount > 1 && NormalizeHex(info.Color) is { } first && NormalizeHex(info.SecondColor) is { } second)
                return $"{first},{second}";

            return null;
        }

        /// <summary>The subtype as a Bambuddy effect type ("Dual Color" → <c>dual-color</c>, "Wood" → <c>wood</c>), when it is one.</summary>
        private static string? EffectType(string? subtype)
        {
            var effect = subtype?.Trim().ToLowerInvariant().Replace(' ', '-');

            return effect != null && BambuddyEffectTypes.Contains(effect) ? effect : null;
        }

        #endregion

        #region Import

        public static LocalSpoolCsvReadResult Read(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return Read(reader);
        }

        /// <summary>
        /// Read spools back from a csv. A row needs a <c>tag_uid</c> or <c>tray_uuid</c> — without one there is nothing
        /// to recognise the spool by on its next scan — so a plain Bambuddy export (which has neither) yields errors only.
        /// </summary>
        public static LocalSpoolCsvReadResult Read(TextReader reader)
        {
            var spools = new List<LocalSpool>();
            var errors = new List<string>();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                PrepareHeaderForMatch = args => NormalizeHeader(args.Header),
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null
            };

            using var csv = new CsvReader(reader, config, leaveOpen: true);

            if (!csv.Read() || !csv.ReadHeader())
            {
                errors.Add("The file is empty.");
                return new LocalSpoolCsvReadResult(spools, errors);
            }

            var headers = (csv.HeaderRecord ?? []).Select(NormalizeHeader).ToHashSet();

            if (!headers.Contains("tag_uid") && !headers.Contains("tray_uuid"))
            {
                errors.Add("The file has no tag_uid or tray_uuid column, so its spools can't be matched to their tags.");
                return new LocalSpoolCsvReadResult(spools, errors);
            }

            var rowNumber = 0;

            while (csv.Read())
            {
                rowNumber++;

                string? Cell(string column) => csv.TryGetField<string>(column, out var value) && !string.IsNullOrWhiteSpace(value) ? Desanitize(value.Trim()) : null;

                try
                {
                    spools.Add(FromRow(Cell));
                }
                catch (FormatException e)
                {
                    errors.Add($"Row {rowNumber}: {e.Message}");
                }
            }

            return new LocalSpoolCsvReadResult(spools, errors);
        }

        private static LocalSpool FromRow(Func<string, string?> cell)
        {
            var tagUid = cell("tag_uid");
            var trayUid = cell("tray_uuid");

            if (tagUid == null && trayUid == null) throw new FormatException("tag_uid or tray_uuid is required");
            if (tagUid != null && (tagUid.Length % 2 != 0 || !tagUid.All(Uri.IsHexDigit))) throw new FormatException($"tag_uid must be hex (got '{tagUid}')");

            var material = cell("material");
            var labelWeight = ParseUShort(cell, "label_weight");

            var info = new BambuFilamentInfo
            {
                TrayUid = trayUid?.ToUpperInvariant(),
                MaterialVariantIdentifier = cell("variant_id"),
                // slicer_filament is "G" + the material id, so a Bambuddy-side edit that only kept it still resolves.
                UniqueMaterialIdentifier = cell("material_id") ?? (cell("slicer_filament") is { Length: > 1 } slicer && slicer.StartsWith('G') ? slicer[1..] : null),
                FilamentType = cell("filament_type") ?? material,
                DetailedFilamentType = cell("detailed_filament_type"),
                Color = ParseColor(cell, "rgba"),
                SpoolWeight = labelWeight,
                FilamentDiameter = ParseFloat(cell, "diameter"),
                SpoolWidth = ParseUShort(cell, "spool_width"),
                FilamentLength = ParseUShort(cell, "filament_length"),
                DryingTemperature = ParseUShort(cell, "drying_temperature"),
                DryingTime = ParseUShort(cell, "drying_time"),
                BedTemperature = ParseUShort(cell, "bed_temperature"),
                MinTemperatureForHotend = ParseUShort(cell, "nozzle_temp_min"),
                MaxTemperatureForHotend = ParseUShort(cell, "nozzle_temp_max"),
                NozzleDiameter = ParseFloat(cell, "nozzle_diameter"),
                ProductionDateTime = ParseDate(cell, "production_date"),
                ColorCount = ParseUShort(cell, "color_count"),
                SecondColor = ParseColor(cell, "second_color")
            };

            if (tagUid != null) info.SerialNumber = tagUid.ToUpperInvariant();

            // Same shape BambuFilamentInfo.ParseData derives it in.
            if (info.MaterialVariantIdentifier != null)
                info.SkuStart = $"{info.MaterialVariantIdentifier}-{$"{info.FilamentDiameter:0.00}".Replace(",", ".")}-{info.SpoolWeight:####}";

            var price = ParseDecimal(cell, "price");
            var costPerKg = ParseDecimal(cell, "cost_per_kg");
            var extraColors = cell("extra_colors")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            var now = DateTime.Now;

            return new LocalSpool
            {
                Info = info,
                Manufacturer = cell("manufacturer"),
                Name = cell("filament_name"),
                FilamentCode = cell("filament_code"),
                ColorHexes = extraColors.Length > 1 ? extraColors.Select(x => x.TrimStart('#').ToUpperInvariant()).ToList() : [],
                Price = price ?? (costPerKg * BambuddyManager.LabelWeight(info) / 1000m),
                Location = cell("storage_location"),
                FirstScanned = ParseDate(cell, "first_scanned") ?? now,
                LastScanned = ParseDate(cell, "last_scanned") ?? now
            };
        }

        #endregion

        #region Helpers

        /// <summary>Case- and separator-tolerant like Bambuddy's: "Color Name", "color-name" and " COLOR_NAME " all match.</summary>
        private static string NormalizeHeader(string header) => header.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

        private static string? Sanitize(string? value) => !string.IsNullOrEmpty(value) && FormulaPrefixes.Contains(value[0]) ? "'" + value : value;

        /// <summary>Exact inverse of <see cref="Sanitize"/>: only a quote that <see cref="Sanitize"/> could have added is removed.</summary>
        private static string Desanitize(string value) => value.Length >= 2 && value[0] == '\'' && FormulaPrefixes.Contains(value[1]) ? value[1..] : value;

        /// <summary>A tag colour as 8-char <c>RRGGBBAA</c>, or null when it isn't one. Bambuddy's rgba and extra_colors take this form as is.</summary>
        private static string? NormalizeHex(string? value)
        {
            var hex = value?.Trim().TrimStart('#').ToUpperInvariant();

            if (hex == null) return null;
            if (hex.Length == 6) hex += "FF";

            return IsHex(hex, 8) ? hex : null;
        }

        private static bool IsHex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);

        private static string? Format(IFormattable? value) => value?.ToString(null, CultureInfo.InvariantCulture);

        private static string? ParseColor(Func<string, string?> cell, string column)
        {
            var value = cell(column);

            if (value == null) return null;

            return NormalizeHex(value) ?? throw new FormatException($"{column} must be 6 or 8 hex characters (got '{value}')");
        }

        private static ushort? ParseUShort(Func<string, string?> cell, string column)
        {
            var value = cell(column);

            if (value == null) return null;

            // Whole numbers, but tolerate "1000.0" from a spreadsheet round trip.
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= 0 && number <= ushort.MaxValue && number == decimal.Truncate(number))
                return (ushort)number;

            throw new FormatException($"{column} must be a whole number (got '{value}')");
        }

        private static float? ParseFloat(Func<string, string?> cell, string column)
        {
            var value = cell(column);

            if (value == null) return null;

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : throw new FormatException($"{column} must be a number (got '{value}')");
        }

        private static decimal? ParseDecimal(Func<string, string?> cell, string column)
        {
            var value = cell(column);

            if (value == null) return null;

            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : throw new FormatException($"{column} must be a number (got '{value}')");
        }

        private static DateTime? ParseDate(Func<string, string?> cell, string column)
        {
            var value = cell(column);

            if (value == null) return null;

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new FormatException($"{column} must be a date (got '{value}')");
        }

        #endregion
    }
}
