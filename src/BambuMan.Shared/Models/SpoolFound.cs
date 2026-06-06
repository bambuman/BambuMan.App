namespace BambuMan.Shared.Models
{
    /// <summary>
    /// Backend-neutral result of inventorying a spool (created or matched). Drives the inventory counter
    /// and prefills the edit panel. The active manager retains the backend-specific reference internally.
    /// </summary>
    public record SpoolFound(
        string? Material,
        string? TrayUid,
        decimal? Weight,        // current total measured weight (g)
        decimal? EmptyWeight,   // empty/core spool weight (g)
        decimal? Price,
        DateTime? BuyDate,
        string? LotNr,
        string? Location);
}
