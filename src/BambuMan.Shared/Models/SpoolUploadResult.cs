using BambuMan.Shared.Enums;

namespace BambuMan.Shared.Models
{
    /// <summary>
    /// The outcome of sending one stored spool to an inventory server. Carries the whole <see cref="LocalSpool"/>, so a
    /// summary can show any of its fields.
    /// </summary>
    /// <param name="Message">Why it failed; for a sent spool a warning worth showing (e.g. it was added as an unknown filament), else null.</param>
    public record SpoolUploadResult(LocalSpool Spool, SpoolUploadStatus Status, string? Message = null);

    /// <summary>Sending progress: <paramref name="Done"/> of <paramref name="Total"/> spools handled, <paramref name="Current"/> the one being sent now (null once finished).</summary>
    public record SpoolUploadProgress(int Done, int Total, LocalSpool? Current);
}
