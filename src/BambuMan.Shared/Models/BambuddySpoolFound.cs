namespace BambuMan.Shared.Models
{
    /// <summary>
    /// Lightweight UI-facing payload for a Bambuddy spool that was inventoried (created or matched).
    /// Mapped from the generated <c>SpoolResponse</c>.
    /// </summary>
    public record BambuddySpoolFound(
        int Id,
        string? Material,
        string? TrayUid,
        string? Brand,
        string? ColorName,
        decimal? WeightUsed,
        int? LabelWeight,
        int? CoreWeight);
}
