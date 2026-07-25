namespace BambuMan.Shared.Enums
{
    /// <summary>
    /// The inventory backend a spool is read/written against. Declaration order drives the settings
    /// segmented-button order (Bambuddy first). Persisted by name, so this order is safe to change.
    /// </summary>
    public enum InventoryBackend
    {
        Bambuddy,
        Spoolman,

        /// <summary>No inventory server at all — scanned tags are shown read-only and nothing is written.</summary>
        NoBackend
    }

    public static class InventoryBackendExtensions
    {
        /// <summary>
        /// How the backend is labelled in the ui. Presentation only — the setting is still persisted with
        /// <see cref="Enum.ToString()"/>, so renaming a label here never invalidates a saved choice.
        /// </summary>
        public static string DisplayName(this InventoryBackend backend) => backend switch
        {
            InventoryBackend.NoBackend => "No backend",
            _ => backend.ToString()
        };
    }
}
