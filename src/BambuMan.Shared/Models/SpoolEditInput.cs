namespace BambuMan.Shared.Models
{
    /// <summary>
    /// Edit-panel submission. The active manager maps the fields it supports and ignores the rest
    /// (e.g. Bambuddy has no Buy date / Lot nr, so those arrive null and are dropped).
    /// </summary>
    public record SpoolEditInput(
        decimal? Weight,        // current total measured weight (g)
        decimal? EmptyWeight,   // empty/core spool weight (g)
        decimal? Price,
        DateTime? BuyDate,
        string? LotNr,
        string? Location);
}
