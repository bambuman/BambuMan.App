using SpoolMan.Api.Model;

namespace BambuMan.Shared.Models
{
    /// <summary>
    /// A spool added by hand because no tag could be read (foil bag, damaged tag, a tag the phone can't unlock).
    /// <see cref="Info"/> is built from what the user picked, so the managers can treat it like a scan — it just has no
    /// tag uid or tray uid, and the picked catalog entry is passed alongside as <see cref="Filament"/>.
    /// </summary>
    /// <param name="Info">Tag-shaped filament data built from the pick. No tag uid, no tray uid.</param>
    /// <param name="Filament">The catalog entry the user picked; null for an unknown filament.</param>
    public record ManualSpool(BambuFilamentInfo Info, ExternalFilament? Filament)
    {
        /// <summary>Filament diameter (mm) assumed for an unknown filament — every Bambu Lab filament is 1.75 mm.</summary>
        public const float DefaultDiameter = 1.75f;

        /// <summary>True when the user didn't find the filament in the catalog and entered it as unknown.</summary>
        public bool IsUnknown => Filament == null;

        /// <summary>A short name for messages and the list, e.g. <c>PLA Matte Ice Blue</c>.</summary>
        public string Label => string.Join(" ", new[] { Info.DetailedFilamentType ?? Info.FilamentType, Filament?.Name }.Where(x => !string.IsNullOrEmpty(x)));

        #region Methods

        /// <summary>A spool of a catalog filament: material, colour(s), label weight and diameter come from the catalog entry.</summary>
        public static ManualSpool FromCatalog(ExternalFilament filament)
        {
            var colors = (filament.ColorHexes ?? []).Select(NormalizeHex).OfType<string>().ToList();
            var primary = NormalizeHex(filament.ColorHex) ?? colors.FirstOrDefault();

            var info = new BambuFilamentInfo
            {
                FilamentType = filament.Material,
                Color = primary == null ? null : primary + "FF",
                ColorCount = (ushort)Math.Max(colors.Count, 1),
                SecondColor = colors.Count > 1 ? colors[1] + "FF" : null,
                SpoolWeight = (ushort)Math.Clamp(filament.Weight, 0, ushort.MaxValue),
                FilamentDiameter = (float)filament.Diameter
            };

            return new ManualSpool(info, filament);
        }

        /// <summary>
        /// A spool of a filament the catalog doesn't have, described by what is printed on its box or label.
        /// </summary>
        /// <param name="material">The material, e.g. <c>PLA</c>. Required.</param>
        /// <param name="colorHex">The colour as <c>RRGGBB</c> (with or without <c>#</c>); anything else is left out.</param>
        /// <param name="labelWeight">Filament weight printed on the label, in grams.</param>
        public static ManualSpool Unknown(string material, string? colorHex, int labelWeight)
        {
            if (string.IsNullOrWhiteSpace(material)) throw new ArgumentException("A material is required", nameof(material));
            if (labelWeight <= 0) throw new ArgumentOutOfRangeException(nameof(labelWeight), labelWeight, "The label weight must be more than 0 g");

            material = material.Trim();

            var color = NormalizeHex(colorHex);

            var info = new BambuFilamentInfo
            {
                FilamentType = material,
                DetailedFilamentType = material,
                Color = color == null ? null : color + "FF",
                ColorCount = 1,
                SpoolWeight = (ushort)Math.Min(labelWeight, ushort.MaxValue),
                FilamentDiameter = DefaultDiameter
            };

            return new ManualSpool(info, null);
        }

        /// <summary>A catalog or picker colour as upper-case <c>RRGGBB</c>, or null when it isn't one.</summary>
        private static string? NormalizeHex(string? value)
        {
            var hex = value?.Trim().TrimStart('#').ToUpperInvariant();

            return hex is { Length: 6 } && hex.All(Uri.IsHexDigit) ? hex : null;
        }

        #endregion
    }
}
