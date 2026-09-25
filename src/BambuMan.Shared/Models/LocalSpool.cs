namespace BambuMan.Shared.Models
{
    /// <summary>
    /// A spool kept by the no-backend mode — the scanned tag plus the catalog match and the defaults it was
    /// inventoried with. The whole <see cref="BambuFilamentInfo"/> is kept so a later export or display can read
    /// any tag field without this record having to grow a property for it.
    /// </summary>
    public class LocalSpool
    {
        #region Properties

        public BambuFilamentInfo Info { get; set; } = new();

        /// <summary>Catalog manufacturer, e.g. <c>Bambu Lab</c>. Null when the tag didn't match the catalog.</summary>
        public string? Manufacturer { get; set; }

        /// <summary>Catalog colour name, e.g. <c>Black</c>. Null when the tag didn't match the catalog.</summary>
        public string? Name { get; set; }

        /// <summary>Bambu filament code, e.g. <c>10101</c>.</summary>
        public string? FilamentCode { get; set; }

        /// <summary>
        /// The catalog's colour stops as <c>RRGGBB</c> for a gradient / dual / multi-colour spool. Empty for a
        /// plain one — the tag's own colour then says it all.
        /// </summary>
        public List<string> ColorHexes { get; set; } = [];

        /// <summary>Price of the whole spool.</summary>
        public decimal? Price { get; set; }

        public string? Location { get; set; }

        public DateTime FirstScanned { get; set; }

        public DateTime LastScanned { get; set; }

        /// <summary>Identity used to de-duplicate — see <see cref="KeyOf"/>.</summary>
        public string? Key => KeyOf(Info);

        #endregion

        #region Methods

        /// <summary>
        /// The tray uid when the tag has one — both tags on a spool share it, so scanning either side finds the same
        /// spool — else the tag uid. Null when neither is present, and such a tag can't be kept.
        /// </summary>
        public static string? KeyOf(BambuFilamentInfo info)
        {
            if (!string.IsNullOrEmpty(info.TrayUid)) return info.TrayUid.ToUpperInvariant();

            var serial = info.SerialNumber;

            return string.IsNullOrEmpty(serial) ? null : serial.ToUpperInvariant();
        }

        #endregion
    }
}
