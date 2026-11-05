using BambuMan.Resources.Strings;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;

namespace BambuMan.UI.WriteTag;

/// <summary>The write-tag form: the OpenSpool 1.0 fields, validated and turned into the spool to write.</summary>
public class WriteTagModel
{
    /// <summary>Suggestions for the material field; any other text is allowed.</summary>
    public static readonly string[] CommonMaterials = ["PLA", "PETG", "ABS", "ASA", "TPU", "PA", "PC", "PVA", "HIPS", "PLA-CF", "PETG-CF", "PA-CF"];

    #region Properties

    public string? Brand { get; set; } = "Generic";

    public string? Material { get; set; } = "PLA";

    /// <summary>As the colour input gives it: <c>#rrggbb</c>.</summary>
    public string Color { get; set; } = "#FFFFFF";

    public int? MinTemperature { get; set; } = 190;

    public int? MaxTemperature { get; set; } = 230;

    public string? MaterialError { get; private set; }

    public string? TemperatureError { get; private set; }

    #endregion

    #region Methods

    /// <summary>Fill the form from a read spool tag of any format.</summary>
    public void CopyFrom(ISpoolTag tag)
    {
        Brand = tag.Brand;
        Material = tag.FilamentType;
        if (tag.Color is { Length: >= 6 } color) Color = $"#{color[..6]}";
        MinTemperature = tag.MinTemperatureForHotend;
        MaxTemperature = tag.MaxTemperatureForHotend;
    }

    public bool Validate()
    {
        MaterialError = string.IsNullOrWhiteSpace(Material) ? AppResources.Common_MaterialRequired : null;

        TemperatureError = MinTemperature is < 0 or > 500 || MaxTemperature is < 0 or > 500 ? AppResources.WriteTag_TemperatureRange
            : MinTemperature > MaxTemperature ? AppResources.WriteTag_TemperatureOrder
            : null;

        return MaterialError == null && TemperatureError == null;
    }

    /// <summary>The spool to write. The tag uid is unknown until a tag is held to the phone, and isn't written anyway.</summary>
    public SpoolTag ToSpoolTag() => new(SpoolTagFormat.OpenSpool, [])
    {
        Brand = string.IsNullOrWhiteSpace(Brand) ? null : Brand.Trim(),
        FilamentType = Material?.Trim().ToUpperInvariant(),
        Color = $"{Color.TrimStart('#').ToUpperInvariant()}FF",
        MinTemperatureForHotend = MinTemperature is > 0 ? (ushort?)MinTemperature.Value : null,
        MaxTemperatureForHotend = MaxTemperature is > 0 ? (ushort?)MaxTemperature.Value : null
    };

    #endregion
}
