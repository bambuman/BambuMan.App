using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Resources.Strings;
using SpoolMan.Api.Model;
using System.Globalization;

namespace BambuMan.Shared.Models
{
    /// <summary>One label/value pair in a read-only spool card.</summary>
    public record SpoolInfoRow(string Label, string Value);

    /// <summary>
    /// A scanned tag rendered for display, for backends that only show what was read (see
    /// <see cref="BaseManager.IsReadOnly"/>). Pure projection — built by <see cref="From"/> and never
    /// mutated, so what the card shows is unit-testable without a ui.
    /// </summary>
    public record SpoolDisplayInfo
    {
        /// <summary>
        /// The spool's colours as <c>#AARRGGBB</c>, in catalog order. One entry for a plain spool, two or more
        /// for a gradient or dual-colour one. Empty when nothing carried a colour.
        /// </summary>
        public IReadOnlyList<string> ColorHexes { get; init; } = [];

        /// <summary>
        /// How the colours sit on the filament, which is what decides how the swatch is drawn — a smooth
        /// left-to-right blend for <c>Longitudinal</c>, hard stacked bands for <c>Coaxial</c>. Null when the
        /// catalog doesn't say (an unmatched tag), which is treated as bands so a second colour still shows.
        /// </summary>
        public SpoolmanExternaldbMultiColorDirection? MultiColorDirection { get; init; }

        /// <summary>The colours written out for the Colour row, e.g. <c>#307FE2, #54FF9B</c>.</summary>
        public string? ColorText { get; init; }

        public bool HasColor => ColorHexes.Count > 0;

        /// <summary>The fields worth showing, in display order. Rows the tag has no value for are omitted.</summary>
        public IReadOnlyList<SpoolInfoRow> Rows { get; init; } = [];

        /// <summary>True when the catalog had no entry for this tag — the rows are then tag data only.</summary>
        public bool Matched { get; init; }

        /// <summary>
        /// The card for a scanned tag of any format. Bambu-only fields (filament code, variant, tray uid, nozzle
        /// diameter) come from a <see cref="BambuFilamentInfo"/>; a third-party tag adds its format, its own brand when
        /// the catalog didn't match, and its message.
        /// </summary>
        public static SpoolDisplayInfo From(ISpoolTag tag, ExternalFilament? matched, FilamentOverrideSet? overrides = null)
        {
            var info = tag as BambuFilamentInfo;
            var thirdParty = tag as SpoolTag;

            var colors = ResolveColors(tag, matched);
            var colorText = colors.Count == 0 ? null : string.Join(", ", colors.Select(ToRgbText));

            var rows = new List<SpoolInfoRow>();

            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) rows.Add(new SpoolInfoRow(label, value));
            }

            Add(SharedResources.SpoolInfo_TagFormat, tag.Format == SpoolTagFormat.BambuLab ? null : tag.Format.DisplayName());
            Add(SharedResources.SpoolInfo_Manufacturer, matched?.Manufacturer ?? (info == null ? tag.Brand : null));
            Add(SharedResources.SpoolInfo_Material, tag.DetailedFilamentType ?? tag.FilamentType);
            Add(SharedResources.SpoolInfo_Name, matched?.Name);
            Add(SharedResources.SpoolInfo_FilamentCode, info == null ? null : ExternalFilamentMatcher.FindFilamentCode(info, overrides));
            Add(SharedResources.SpoolInfo_Variant, info?.MaterialVariantIdentifier);
            Add(SharedResources.SpoolInfo_Manufactured, tag.ProductionDateTime?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            Add(SharedResources.SpoolInfo_Colour, colorText);
            Add(SharedResources.SpoolInfo_ColourType, matched?.MultiColorDirection?.ToString());
            Add(SharedResources.SpoolInfo_Diameter, tag.FilamentDiameter == null ? null : $"{tag.FilamentDiameter.Value.ToString("0.00", CultureInfo.InvariantCulture)} mm");
            Add(SharedResources.SpoolInfo_SpoolWeight, tag.SpoolWeight == null ? null : $"{tag.SpoolWeight} g");
            Add(SharedResources.SpoolInfo_Remaining, thirdParty?.RemainingWeight == null ? null : $"{thirdParty.RemainingWeight} g");
            Add(SharedResources.SpoolInfo_DryingTemperature, tag.DryingTemperature == null ? null : $"{tag.DryingTemperature} °C");
            Add(SharedResources.SpoolInfo_DryingTime, tag.DryingTime == null ? null : $"{tag.DryingTime} h");
            Add(SharedResources.SpoolInfo_HotendTemperature, FormatRange(tag.MinTemperatureForHotend, tag.MaxTemperatureForHotend, "°C"));
            Add(SharedResources.SpoolInfo_BedTemperature, info == null && tag.BedTemperature != null ? $"{tag.BedTemperature} °C" : null);
            Add(SharedResources.SpoolInfo_MinNozzleDiameter, info?.NozzleDiameter == null ? null : $"{info.NozzleDiameter.Value.ToString("0.0#", CultureInfo.InvariantCulture)} mm");
            Add(SharedResources.SpoolInfo_Message, thirdParty?.Message);
            Add(SharedResources.SpoolInfo_TagSerial, tag.SerialNumber);
            Add(SharedResources.SpoolInfo_TrayUid, info?.TrayUid);

            return new SpoolDisplayInfo
            {
                ColorHexes = colors,
                MultiColorDirection = matched?.MultiColorDirection,
                ColorText = colorText,
                Rows = rows,
                Matched = matched != null
            };
        }

        /// <summary>
        /// Where the swatch colours come from, in the same order of preference the web frontend's ColorSwatch
        /// component uses: the catalog's list first — it is the only source that can describe a gradient with
        /// more than two stops — then its single colour, then the tag itself.
        /// </summary>
        private static List<string> ResolveColors(ISpoolTag tag, ExternalFilament? matched)
        {
            if (matched?.ColorHexes is { Count: > 0 } catalogColors)
                return catalogColors.Select(ToArgbHex).OfType<string>().ToList();

            if (ToArgbHex(matched?.ColorHex) is { } catalogColor) return [catalogColor];

            var colors = new List<string>();

            if (ToArgbHex(tag.Color) is { } primary) colors.Add(primary);

            switch (tag)
            {
                // A single-colour spool still carries a SecondColor field, so gate on the count rather than the value.
                case BambuFilamentInfo info when info.ColorCount > 1 && ToArgbHex(info.SecondColor) is { } second:
                    colors.Add(second);
                    break;
                case SpoolTag thirdParty:
                    colors.AddRange(thirdParty.AdditionalColors.Select(ToArgbHex).OfType<string>());
                    break;
            }

            return colors;
        }

        /// <summary>
        /// Normalizes to the <c>#AARRGGBB</c> the ui binds. Tag colours arrive as <c>RRGGBBAA</c> so the opacity
        /// byte moves to the front; catalog colours are plain <c>RRGGBB</c> and become opaque.
        /// </summary>
        private static string? ToArgbHex(string? color)
        {
            if (string.IsNullOrWhiteSpace(color)) return null;

            var hex = color.TrimStart('#');

            return hex.Length switch
            {
                8 => $"#{hex[6..]}{hex[..6]}",
                6 => $"#FF{hex}",
                _ => null
            };
        }

        /// <summary>The rgb part of an <c>#AARRGGBB</c> value, for the caption beside the swatch.</summary>
        private static string ToRgbText(string argbHex) => $"#{argbHex[3..]}";

        private static string? FormatRange(ushort? min, ushort? max, string unit)
        {
            if (min == null && max == null) return null;
            if (min == null || max == null) return $"{min ?? max} {unit}";

            return min == max ? $"{min} {unit}" : $"{min}–{max} {unit}";
        }
    }
}
