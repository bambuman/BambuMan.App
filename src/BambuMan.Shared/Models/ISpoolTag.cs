using BambuMan.Shared.Enums;

namespace BambuMan.Shared.Models
{
    /// <summary>
    /// A scanned spool tag, whatever format it was read from — what the inventory managers, the matcher and the ui
    /// need from any tag. Implemented by <see cref="BambuFilamentInfo"/> (Bambu Lab tags) and <see cref="SpoolTag"/>
    /// (OpenSpool, TigerTag). Property names follow <see cref="BambuFilamentInfo"/>, so the Bambu path is unchanged;
    /// code that needs format-specific fields checks the concrete type.
    /// </summary>
    public interface ISpoolTag
    {
        #region Properties

        SpoolTagFormat Format { get; }

        /// <summary>Tag UID as hex, no separators.</summary>
        string SerialNumber { get; }

        /// <summary>
        /// What the backends recognise the spool by on its next scan: the tray uid of a Bambu Lab tag (shared by both
        /// tags on the spool), the tag uid otherwise. Null when the tag has neither.
        /// </summary>
        string? SpoolId { get; }

        /// <summary>Manufacturer as written on the tag, e.g. <c>Bambu Lab</c>, <c>Generic</c>, <c>Polymaker</c>.</summary>
        string? Brand { get; }

        /// <summary>Base material, e.g. <c>PLA</c>.</summary>
        string? FilamentType { get; }

        /// <summary>Material with its variant, e.g. <c>PLA Matte</c>. Same as <see cref="FilamentType"/> when the tag has no variant.</summary>
        string? DetailedFilamentType { get; }

        /// <summary>Primary colour as <c>RRGGBBAA</c>.</summary>
        string? Color { get; }

        /// <summary>Filament weight on a full spool in grams.</summary>
        ushort? SpoolWeight { get; }

        /// <summary>Filament diameter in millimeters.</summary>
        float? FilamentDiameter { get; }

        ushort? MinTemperatureForHotend { get; }

        ushort? MaxTemperatureForHotend { get; }

        ushort? BedTemperature { get; }

        ushort? DryingTemperature { get; }

        ushort? DryingTime { get; }

        /// <summary>When the spool was made, if the tag says.</summary>
        DateTime? ProductionDateTime { get; }

        #endregion

        #region Methods

        /// <summary>Compact json for diagnostics/telemetry, without the spool-instance identifiers (tag uid, tray uid).</summary>
        string ToDiagnosticJson();

        #endregion
    }
}
