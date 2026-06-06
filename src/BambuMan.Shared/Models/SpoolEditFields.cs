namespace BambuMan.Shared.Models
{
    /// <summary>
    /// Which optional edit-panel fields a backend supports. The UI binds field visibility to these so a
    /// single panel adapts per backend (Weight / Empty weight / Price / Location are always shown).
    /// </summary>
    public record SpoolEditFields(
        bool BuyDate,
        bool LotNr);
}
