namespace BambuMan.Shared.Enums
{
    /// <summary>
    /// The inventory backend a spool is read/written against. Declaration order drives the settings
    /// segmented-button order (Bambuddy first). Persisted by name, so this order is safe to change.
    /// </summary>
    public enum InventoryBackend
    {
        Bambuddy,
        Spoolman
    }
}
