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

        /// <summary>
        /// The tag's fields. A third-party tag is kept in this shape too (see <see cref="SpoolTag.ToFilamentInfo"/>),
        /// with <see cref="Format"/> and <see cref="Brand"/> saying what it really was.
        /// </summary>
        public BambuFilamentInfo Info { get; set; } = new();

        /// <summary>The format the spool's tag was read in. Stored by name; files from before third-party tags read as Bambu Lab.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public SpoolTagFormat Format { get; set; } = SpoolTagFormat.BambuLab;

        /// <summary>Brand written on a third-party tag. Null for a Bambu Lab tag, whose brand is implied.</summary>
        public string? Brand { get; set; }

        /// <summary>The spool's tag as the matcher and the ui take it: <see cref="Info"/> itself, or the third-party tag rebuilt from it.</summary>
        [JsonIgnore]
        public ISpoolTag Tag => Format == SpoolTagFormat.BambuLab ? Info : SpoolTag.FromFilamentInfo(Format, Brand, Info);

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

        /// <summary>
        /// Identity of a spool added by hand (see <see cref="ManualSpool"/>), which has no tag to be keyed by. Null for a
        /// scanned spool.
        /// </summary>
        public string? ManualId { get; set; }

        /// <summary>
        /// Catalog id of the filament a spool added by hand was picked as, e.g. <c>bambulab_pla_black_1000_175_n</c>. A
        /// scanned spool is matched by its tag instead and leaves this null, as does a hand-added unknown filament.
        /// </summary>
        public string? FilamentId { get; set; }

        /// <summary>True for a spool added by hand rather than scanned.</summary>
        [JsonIgnore]
        public bool IsManual => ManualId != null && !Info.HasTag;

        /// <summary>Identity used to de-duplicate — see <see cref="KeyOf"/>, and <see cref="ManualId"/> for a spool added by hand.</summary>
        public string? Key => KeyOf(Info) ?? ManualId?.ToUpperInvariant();

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

        /// <summary>A new <see cref="ManualId"/>. Prefixed, so it can't be mistaken for (or collide with) a tag's hex uid.</summary>
        public static string NewManualId() => $"MANUAL-{Guid.NewGuid():N}".ToUpperInvariant();

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
