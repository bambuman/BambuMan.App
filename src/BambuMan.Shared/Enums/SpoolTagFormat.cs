namespace BambuMan.Shared.Enums
{
    /// <summary>
    /// The on-tag format a spool was read from. Persisted by name (local inventory, csv <c>tag_type</c>), so the
    /// declaration order is safe to change.
    /// </summary>
    public enum SpoolTagFormat
    {
        /// <summary>Bambu Lab MIFARE Classic 1K tag (encrypted sectors, keys derived from the uid).</summary>
        BambuLab,

        /// <summary>OpenSpool: an NTAG215/216 with one NDEF <c>application/json</c> record.</summary>
        OpenSpool,

        /// <summary>TigerTag: an NTAG21x with a fixed binary layout in pages 0x04–0x27 and id lookups.</summary>
        TigerTag
    }

    public static class SpoolTagFormatExtensions
    {
        /// <summary>How the format is labelled in the ui. Presentation only.</summary>
        public static string DisplayName(this SpoolTagFormat format) => format switch
        {
            SpoolTagFormat.BambuLab => "Bambu Lab",
            _ => format.ToString()
        };

        /// <summary>
        /// Bambuddy's <c>tag_type</c> for the format, also written to the csv export. <c>bambu_rfid</c> is what
        /// Bambuddy itself uses; the other two are BambuMan's own lower-case names.
        /// </summary>
        public static string TagType(this SpoolTagFormat format) => format switch
        {
            SpoolTagFormat.BambuLab => "bambu_rfid",
            _ => format.ToString().ToLowerInvariant()
        };

        /// <summary>Inverse of <see cref="TagType"/>; null when the value names no known format.</summary>
        public static SpoolTagFormat? FromTagType(string? tagType)
        {
            if (string.IsNullOrWhiteSpace(tagType)) return null;

            foreach (var format in Enum.GetValues<SpoolTagFormat>())
            {
                if (format.TagType().Equals(tagType.Trim(), StringComparison.OrdinalIgnoreCase)) return format;
            }

            return Enum.TryParse<SpoolTagFormat>(tagType.Trim(), true, out var parsed) ? parsed : null;
        }
    }
}
