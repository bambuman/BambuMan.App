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
        /// <summary>The tag colour as <c>#AARRGGBB</c>, ready to bind to a swatch. Null when the tag has none.</summary>
        public string? ColorHex { get; init; }

        /// <summary>The second colour of a dual-colour spool, or null when the spool has only one.</summary>
        public string? SecondColorHex { get; init; }

        /// <summary>The fields worth showing, in display order. Rows the tag has no value for are omitted.</summary>
        public IReadOnlyList<SpoolInfoRow> Rows { get; init; } = [];

        /// <summary>True when the catalog had no entry for this tag — the rows are then tag data only.</summary>
        public bool Matched { get; init; }

        public static SpoolDisplayInfo From(BambuFilamentInfo info, ExternalFilament? matched, FilamentOverrideSet? overrides = null)
        {
            var rows = new List<SpoolInfoRow>();

            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) rows.Add(new SpoolInfoRow(label, value));
            }

            Add("Manufacturer", matched?.Manufacturer);
            Add("Material", info.DetailedFilamentType ?? info.FilamentType);
            Add("Name", matched?.Name);
            Add("Colour", FormatColorText(info.Color));
            Add("Variant", info.MaterialVariantIdentifier);
            Add("Filament code", ExternalFilamentMatcher.FindFilamentCode(info, overrides));
            Add("Diameter", info.FilamentDiameter == null ? null : $"{info.FilamentDiameter.Value.ToString("0.00", CultureInfo.InvariantCulture)} mm");
            Add("Spool weight", info.SpoolWeight == null ? null : $"{info.SpoolWeight} g");
            Add("Manufactured", info.ProductionDateTime?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            Add("Drying temperature", info.DryingTemperature == null ? null : $"{info.DryingTemperature} °C");
            Add("Drying time", info.DryingTime == null ? null : $"{info.DryingTime} h");
            Add("Hotend temperature", FormatRange(info.MinTemperatureForHotend, info.MaxTemperatureForHotend, "°C"));
            Add("Nozzle diameter", info.NozzleDiameter == null ? null : $"{info.NozzleDiameter.Value.ToString("0.0#", CultureInfo.InvariantCulture)} mm");
            Add("Tag serial", info.SerialNumber);
            Add("Tray uid", info.TrayUid);

            return new SpoolDisplayInfo
            {
                ColorHex = ToArgbHex(info.Color),
                // A single-colour spool still carries a SecondColor field, so gate on the count rather than the value.
                SecondColorHex = info.ColorCount > 1 ? ToArgbHex(info.SecondColor) : null,
                Rows = rows,
                Matched = matched != null
            };
        }

        /// <summary>
        /// The tag stores colours as <c>RRGGBBAA</c>; MAUI's colour converter reads <c>#AARRGGBB</c>, so the
        /// opacity byte moves to the front. A six-character value is passed through as opaque.
        /// </summary>
        private static string? ToArgbHex(string? tagColor)
        {
            if (string.IsNullOrWhiteSpace(tagColor)) return null;

            return tagColor.Length switch
            {
                8 => $"#{tagColor[6..]}{tagColor[..6]}",
                6 => $"#{tagColor}",
                _ => null
            };
        }

        /// <summary>The colour as shown in the row list — the rgb hex, with opacity appended only when it matters.</summary>
        private static string? FormatColorText(string? tagColor)
        {
            if (string.IsNullOrWhiteSpace(tagColor) || tagColor.Length < 6) return null;

            var rgb = $"#{tagColor[..6]}";

            if (tagColor.Length < 8) return rgb;

            var opacity = tagColor[6..];

            return opacity.EqualsCI("FF") ? rgb : $"{rgb} ({Convert.ToInt32(opacity, 16) * 100 / 255}% opaque)";
        }

        private static string? FormatRange(ushort? min, ushort? max, string unit)
        {
            if (min == null && max == null) return null;
            if (min == null || max == null) return $"{min ?? max} {unit}";

            return min == max ? $"{min} {unit}" : $"{min}–{max} {unit}";
        }
    }
}
