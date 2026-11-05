using BambuMan.Shared.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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

        /// <summary>Measured weight of the whole spool (filament + empty spool) in grams, as entered in the edit card. Null until weighed.</summary>
        public decimal? Weight { get; set; }

        /// <summary>Weight of the empty spool in grams. Null means the standard Bambu spool core.</summary>
        public decimal? EmptyWeight { get; set; }

        public DateTime? BuyDate { get; set; }

        public string? LotNr { get; set; }

        public DateTime FirstScanned { get; set; }

        public DateTime LastScanned { get; set; }

        /// <summary>
        /// The inventory server this spool was last sent to, with its fields as they are now. Cleared when the spool is
        /// edited afterwards, so it reads as not sent until it is sent again. Stored by name.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public InventoryBackend? SentTo { get; set; }

        /// <summary>When the spool was sent to <see cref="SentTo"/>.</summary>
        public DateTime? SentAt { get; set; }

        /// <summary>Identity used to de-duplicate — see <see cref="KeyOf"/>.</summary>
        public string? Key => KeyOf(Info);

        /// <summary>The edit card's fields as this spool has them.</summary>
        [JsonIgnore]
        public SpoolEditInput EditInput => new(Weight, EmptyWeight, Price, BuyDate, LotNr, Location);

        /// <summary>Material and catalog colour name, e.g. <c>PLA Basic Black</c>, for lists and messages.</summary>
        [JsonIgnore]
        public string Title
        {
            get
            {
                var material = Info.DetailedFilamentType ?? Info.FilamentType;

                return Name == null ? material ?? "Unknown filament" : $"{material} {Name}";
            }
        }

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

        /// <summary>
        /// The edit-card input that brings a server's spool in line with this one: every field this spool has a value
        /// for wins, every field it has none for keeps what the server has (<paramref name="current"/>) — so an unweighed
        /// stored spool doesn't reset the server's remaining weight, and a missing price doesn't clear the server's.
        /// </summary>
        public SpoolEditInput ToEditInput(SpoolFound current) => new(
            Weight: Weight ?? current.Weight,
            EmptyWeight: EmptyWeight ?? current.EmptyWeight,
            Price: Price ?? current.Price,
            BuyDate: BuyDate ?? current.BuyDate,
            LotNr: LotNr ?? current.LotNr,
            Location: Location ?? current.Location);

        #endregion
    }
}
