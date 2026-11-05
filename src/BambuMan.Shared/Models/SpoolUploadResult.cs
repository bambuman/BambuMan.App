using BambuMan.Shared.Resources.Strings;
using BambuMan.Shared.Enums;

namespace BambuMan.Shared.Models
{
    /// <summary>
    /// The outcome of sending one stored spool to an inventory server. Carries the whole <see cref="LocalSpool"/>, so a
    /// summary can show any of its fields.
    /// </summary>
    /// <param name="Message">Why it failed; for a sent spool a warning worth showing (e.g. it was added as an unknown filament), else null.</param>
    /// <param name="KeptServerWeight">
    /// The weight the server's spool kept instead of the stored one, which would have left more filament on it than the server has
    /// (see <see cref="Managers.BaseManager.UploadSpoolAsync"/>); null when the stored weight, if any, was written.
    /// </param>
    public record SpoolUploadResult(LocalSpool Spool, SpoolUploadStatus Status, string? Message = null, decimal? KeptServerWeight = null)
    {
        /// <summary>The outcome as the send summary lists it, e.g. <c>Updated (kept server weight: 600 g)</c>.</summary>
        public string StatusText => KeptServerWeight is { } weight ? string.Format(SharedResources.Upload_StatusKeptServerWeight, StatusName, $"{weight:0.#}") : StatusName;

        private string StatusName => Status switch
        {
            SpoolUploadStatus.Created => SharedResources.Upload_StatusCreated,
            SpoolUploadStatus.Updated => SharedResources.Upload_StatusUpdated,
            _ => SharedResources.Upload_StatusFailed
        };
    }

    /// <summary>Sending progress: <paramref name="Done"/> of <paramref name="Total"/> spools handled, <paramref name="Current"/> the one being sent now (null once finished).</summary>
    public record SpoolUploadProgress(int Done, int Total, LocalSpool? Current);
}
