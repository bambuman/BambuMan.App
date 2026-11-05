using BambuMan.Shared.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BambuMan.Shared.Models
{
    /// <summary>
    /// A scanned tag on its way to an inventory backend, with the import defaults it is inventoried with. A live scan
    /// is sent straight away; one the backend couldn't take (see <see cref="ScanSendStatus.Unreachable"/>) waits in the
    /// pending scan queue and is sent the same way once the backend is back. The tag is kept the way a stored spool keeps
    /// it (see <see cref="LocalSpool.Info"/>): a Bambu Lab tag whole, a third-party one in the Bambu tag's shape with its
    /// <see cref="Format"/> and <see cref="Brand"/>.
    /// </summary>
    public class PendingScan
    {
        #region Properties

        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The backend the scan was made for. A queued scan is only ever sent there. Stored by name.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public InventoryBackend Backend { get; set; }

        /// <summary>The tag's fields; for a third-party tag see <see cref="SpoolTag.ToFilamentInfo"/>.</summary>
        public BambuFilamentInfo Info { get; set; } = new();

        /// <summary>The format the tag was read in. Stored by name; queues from before third-party tags read as Bambu Lab.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public SpoolTagFormat Format { get; set; } = SpoolTagFormat.BambuLab;

        /// <summary>Brand written on a third-party tag. Null for a Bambu Lab tag, whose brand is implied.</summary>
        public string? Brand { get; set; }

        /// <summary>
        /// The scanned tag as the managers take it; setting it fills <see cref="Info"/>, <see cref="Format"/> and <see cref="Brand"/>.
        /// A third-party tag set here is handed out as read (all its fields, e.g. the remaining weight) while this scan is in memory;
        /// once read back from the queue file it is rebuilt from <see cref="Info"/>.
        /// </summary>
        [JsonIgnore]
        public ISpoolTag Tag
        {
            get
            {
                if (Format == SpoolTagFormat.BambuLab) return Info;

                return scannedTag is { } read && ReferenceEquals(scannedInfo, Info) ? read : SpoolTag.FromFilamentInfo(Format, Brand, Info);
            }
            set
            {
                Info = value switch
                {
                    BambuFilamentInfo bambu => bambu,
                    SpoolTag thirdParty => thirdParty.ToFilamentInfo(),
                    _ => throw new NotSupportedException($"Unsupported spool tag {value.GetType().Name}")
                };

                Format = value.Format;
                Brand = value.Format == SpoolTagFormat.BambuLab ? null : value.Brand;
                scannedTag = value as SpoolTag;
                scannedInfo = Info;
            }
        }

        // The third-party tag as read, and the Info built from it (a later Info replaces it).
        private SpoolTag? scannedTag;
        private BambuFilamentInfo? scannedInfo;

        public DateTime? BuyDate { get; set; }

        public decimal? Price { get; set; }

        public string? LotNr { get; set; }

        public string? Location { get; set; }

        /// <summary>When the scan went into the queue. Null for a live scan that was never queued.</summary>
        public DateTime? QueuedAt { get; set; }

        /// <summary>
        /// Why the backend rejected the scan the last time it was sent. Null while it is only waiting for the backend.
        /// A rejected scan is kept for the user to retry or discard, but isn't sent again automatically.
        /// </summary>
        public string? LastError { get; set; }

        /// <summary>Same identity as a stored spool — see <see cref="LocalSpool.KeyOf"/>.</summary>
        public string? Key => LocalSpool.KeyOf(Info);

        public bool IsQueued => QueuedAt != null;

        public bool IsRejected => LastError != null;

        #endregion
    }

    /// <summary>The outcome of sending one <see cref="PendingScan"/>; <paramref name="Error"/> says why it was rejected.</summary>
    public record ScanSendResult(ScanSendStatus Status, string? Error = null);
}
