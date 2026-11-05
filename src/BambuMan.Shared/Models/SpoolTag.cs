using BambuMan.Shared.Enums;
using BambuMan.Shared.Nfc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BambuMan.Shared.Models
{
    /// <summary>
    /// A spool tag that isn't a Bambu Lab one (OpenSpool, TigerTag), parsed into one format-neutral shape by
    /// <see cref="OpenSpool"/> and <see cref="TigerTag"/>. There is no tray uid on these tags, so the tag uid is the
    /// spool's identity (<see cref="SpoolId"/>). The raw payload is kept alongside for diagnostics.
    /// </summary>
    public class SpoolTag : ISpoolTag, ITagInfo
    {
        public SpoolTag() { }

        public SpoolTag(SpoolTagFormat format, byte[] identifier)
        {
            Format = format;
            Identifier = identifier;
        }

        #region Properties

        public SpoolTagFormat Format { get; set; }

        [JsonIgnore]
        public byte[] Identifier { get; private set; } = [];

        /// <summary>Tag UID</summary>
        public string SerialNumber
        {
            get => BitConverter.ToString(Identifier).Replace("-", "");
            set => Identifier = value.StringToByteArray();
        }

        /// <summary>The tag uid — these formats carry no spool-level id.</summary>
        [JsonIgnore]
        public string? SpoolId => Identifier.Length == 0 ? null : SerialNumber;

        public string? Brand { get; set; }

        public string? FilamentType { get; set; }

        /// <summary>Variant / finish, e.g. <c>Matte</c>, <c>Silk</c>. Null when the tag doesn't say.</summary>
        public string? Subtype { get; set; }

        [JsonIgnore]
        public string? DetailedFilamentType => string.IsNullOrWhiteSpace(Subtype) ? FilamentType : $"{FilamentType} {Subtype}".Trim();

        /// <summary>Primary colour as <c>RRGGBBAA</c>.</summary>
        public string? Color { get; set; }

        /// <summary>Further colour stops (<c>RRGGBBAA</c>) of a multi-colour spool, after <see cref="Color"/>. Empty for a plain one.</summary>
        public List<string> AdditionalColors { get; set; } = [];

        public ushort? SpoolWeight { get; set; }

        /// <summary>Filament left on the spool in grams, when the tag tracks it (TigerTag's "measure available").</summary>
        public ushort? RemainingWeight { get; set; }

        public float? FilamentDiameter { get; set; }

        public ushort? MinTemperatureForHotend { get; set; }

        public ushort? MaxTemperatureForHotend { get; set; }

        public ushort? BedTemperature { get; set; }

        public ushort? DryingTemperature { get; set; }

        public ushort? DryingTime { get; set; }

        public DateTime? ProductionDateTime { get; set; }

        /// <summary>Free text written on the tag (TigerTag's custom message).</summary>
        public string? Message { get; set; }

        /// <summary>The format's own product id when it has one (TigerTag+), else null.</summary>
        public long? ProductId { get; set; }

        /// <summary>The tag payload as read: the OpenSpool json, or the TigerTag user memory as hex.</summary>
        [JsonIgnore]
        public string? RawPayload { get; set; }

        #endregion

        #region ITagInfo

        [JsonIgnore]
        public bool IsWritable { get; set; }

        [JsonIgnore]
        public bool IsEmpty => false;

        [JsonIgnore]
        public bool IsSupported => true;

        [JsonIgnore]
        public int Capacity { get; set; }

        [JsonIgnore]
        public NfcNdefRecord?[]? Records { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Compact json for diagnostics/telemetry, with the tag uid and the free-text message removed so neither
        /// leaves the device. The filament attributes stay, for debugging match failures.
        /// </summary>
        public string ToDiagnosticJson()
        {
            var obj = JObject.FromObject(this);
            obj.Remove(nameof(SerialNumber));
            obj.Remove(nameof(Message));
            return obj.ToString(Formatting.None);
        }

        /// <summary>
        /// The tag's fields as a <see cref="BambuFilamentInfo"/>, for the local inventory and its csv, which store tag
        /// data in that shape (the csv import does the same for a row). No tray uid, so the spool keys by tag uid.
        /// </summary>
        public BambuFilamentInfo ToFilamentInfo()
        {
            var info = new BambuFilamentInfo(Identifier)
            {
                FilamentType = FilamentType,
                DetailedFilamentType = DetailedFilamentType,
                Color = Color,
                SpoolWeight = SpoolWeight,
                FilamentDiameter = FilamentDiameter,
                MinTemperatureForHotend = MinTemperatureForHotend,
                MaxTemperatureForHotend = MaxTemperatureForHotend,
                BedTemperature = BedTemperature,
                DryingTemperature = DryingTemperature,
                DryingTime = DryingTime,
                ProductionDateTime = ProductionDateTime
            };

            if (AdditionalColors.Count > 0)
            {
                info.ColorCount = (ushort)(AdditionalColors.Count + 1);
                info.SecondColor = AdditionalColors[0];
            }

            return info;
        }

        /// <summary>Inverse of <see cref="ToFilamentInfo"/>, for a stored spool of this format.</summary>
        public static SpoolTag FromFilamentInfo(SpoolTagFormat format, string? brand, BambuFilamentInfo info)
        {
            var type = info.FilamentType;
            var detailed = info.DetailedFilamentType;
            var subtype = type != null && detailed != null && detailed.Length > type.Length && detailed.StartsWith(type + " ", StringComparison.OrdinalIgnoreCase) ? detailed[(type.Length + 1)..] : null;

            return new SpoolTag(format, info.Identifier)
            {
                Brand = brand,
                FilamentType = type,
                Subtype = subtype,
                Color = info.Color,
                AdditionalColors = info.ColorCount > 1 && info.SecondColor != null ? [info.SecondColor] : [],
                SpoolWeight = info.SpoolWeight,
                FilamentDiameter = info.FilamentDiameter,
                MinTemperatureForHotend = info.MinTemperatureForHotend,
                MaxTemperatureForHotend = info.MaxTemperatureForHotend,
                BedTemperature = info.BedTemperature,
                DryingTemperature = info.DryingTemperature,
                DryingTime = info.DryingTime,
                ProductionDateTime = info.ProductionDateTime
            };
        }

        #endregion
    }
}
