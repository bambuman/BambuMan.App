using BambuMan.Shared.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BambuMan.Shared.Models
{
    /// <summary>
    /// A scanned tag on its way to an inventory backend, with the import defaults it is inventoried with. A live scan
    /// is sent straight away; one the backend couldn't take (see <see cref="ScanSendStatus.Unreachable"/>) waits in the
    /// pending scan queue and is sent the same way once the backend is back. The whole <see cref="BambuFilamentInfo"/>
    /// is kept so the queued scan is exactly what was read.
    /// </summary>
    public class PendingScan
    {
        #region Properties

        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The backend the scan was made for. A queued scan is only ever sent there. Stored by name.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public InventoryBackend Backend { get; set; }

        public BambuFilamentInfo Info { get; set; } = new();

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
