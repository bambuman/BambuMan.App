namespace BambuMan.Shared.Models
{
    /// <summary>
    /// Which scanned tags an override applies to. Every field is optional and all supplied fields must
    /// match (AND); a null field means "don't care". <see cref="Color"/> matches either the 6-character
    /// hex prefix or the full 8-character tag colour (hex + opacity).
    /// </summary>
    public record FilamentMatchCriteria(
        string? MaterialVariantIdentifier = null,
        string? UniqueMaterialIdentifier = null,
        string? FilamentType = null,
        string? DetailedFilamentType = null,
        string? Color = null,
        bool? Transparent = null);

    /// <summary>Force a matching tag to resolve to exactly one catalog filament.</summary>
    public record ForcedIdOverride(FilamentMatchCriteria Criteria, string FilamentId);

    /// <summary>
    /// Accept a catalog entry whose <c>ColorHex</c> differs from the colour stored on the tag —
    /// the spoolman external db and the Bambu tag disagree on a handful of SKUs.
    /// </summary>
    public record ColorHexOverride(FilamentMatchCriteria Criteria, string CatalogColorHex);

    /// <summary>The corrected colour pair to look for when a multi-colour spool reports the wrong colours.</summary>
    public record MultiColorOverride(FilamentMatchCriteria Criteria, string[] Colors);

    /// <summary>Restrict matching to catalog entries carrying one of these names.</summary>
    public record NameFilterOverride(FilamentMatchCriteria Criteria, string[] Names);

    /// <summary>
    /// The per-SKU exceptions <see cref="ExternalFilamentMatcher"/> applies on top of its generic
    /// material-family rules. Compiled into every build (see <see cref="FilamentMatchOverrides.Internal"/>)
    /// and also served by the BambuMan API so a correction can ship without an app release.
    /// <para>
    /// A set is used whole — the app runs whichever of internal/remote has the higher <see cref="Version"/>,
    /// never a mixture. Properties default to empty so a set deserialized from an older or newer peer
    /// never yields a null list.
    /// </para>
    /// </summary>
    public record FilamentOverrideSet
    {
        /// <summary>Bumped by hand whenever the internal set is edited. Drives which set wins.</summary>
        public int Version { get; init; }

        /// <summary>
        /// Support-material forced ids. Kept separate from <see cref="ForcedIds"/> because these are
        /// resolved earlier in the matcher (before the filament-type filters) and that order is load-bearing.
        /// </summary>
        public ForcedIdOverride[] SupportForcedIds { get; init; } = [];

        /// <summary>Forced ids applied last. Later entries win, matching the original sequential if-chain.</summary>
        public ForcedIdOverride[] ForcedIds { get; init; } = [];

        public ColorHexOverride[] ColorHexes { get; init; } = [];

        /// <summary>First match wins.</summary>
        public MultiColorOverride[] MultiColors { get; init; } = [];

        /// <summary>First match wins, so a broader entry can act as the fallback for a narrower one above it.</summary>
        public NameFilterOverride[] NameFilters { get; init; } = [];

        /// <summary>Catalog ids that are translucent despite the catalog not saying so.</summary>
        public string[] TransparentFilamentIds { get; init; } = [];

        /// <summary>Total number of override entries — for logging. Not part of the wire format.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int Count => SupportForcedIds.Length + ForcedIds.Length + ColorHexes.Length + MultiColors.Length + NameFilters.Length + TransparentFilamentIds.Length;
    }
}
