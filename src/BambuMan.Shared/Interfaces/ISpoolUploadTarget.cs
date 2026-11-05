using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;

namespace BambuMan.Shared.Interfaces
{
    /// <summary>
    /// An inventory server the spools kept on the device can be sent to (Spoolman, Bambuddy). The server-backed managers
    /// implement it with <see cref="Managers.BaseManager.UploadSpoolAsync"/>.
    /// </summary>
    public interface ISpoolUploadTarget
    {
        /// <summary>Which backend the spools go to; kept on a sent spool as <see cref="LocalSpool.SentTo"/>.</summary>
        InventoryBackend Backend { get; }

        /// <summary>
        /// Create or update the server's spool for a stored spool the way a scan of its tag would, then apply the stored
        /// spool's own fields. A spool the server rejects is a <see cref="SpoolUploadStatus.Failed"/> result, not an exception.
        /// </summary>
        Task<SpoolUploadResult> UploadSpoolAsync(LocalSpool spool);
    }
}
