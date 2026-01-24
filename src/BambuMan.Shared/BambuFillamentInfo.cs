using BambuMan.Shared.Nfc;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;

namespace BambuMan.Shared;

public class BambuFillamentInfo : ITagInfo
{
    public BambuFillamentInfo() { }

    public BambuFillamentInfo(byte[] identifier)
    {
        Identifier = identifier;
    }

    [JsonIgnore]
    public byte[] Identifier { get; private set; } = [];

    /// <summary>
    /// Tag UID
    /// </summary>
    public string SerialNumber
    {
        get => BitConverter.ToString(Identifier).Replace("-", "");
        set => Identifier = value.StringToByteArray();
    }

    [JsonIgnore]
    public bool IsWritable { get; set; } = false;

    [JsonIgnore]
    public bool IsEmpty => false;

    [JsonIgnore]
    public bool IsSupported => false;

    [JsonIgnore]
    public int Capacity { get; set; }

    [JsonIgnore]
    public NfcNdefRecord?[]? Records { get; set; } = [];

    /// <summary>
    /// Tag Manufacturer Data 
    /// </summary>
    public byte[]? TagManufacturerData { get; set; }

    /// <summary>
    /// Tray Info Index - Material Variant Identifier
    /// </summary>
    public string? MaterialVariantIdentifier { get; set; }

    /// <summary>
    /// Tray Info Index - Unique Material Identifier
    /// </summary>
    public string? UniqueMaterialIdentifier { get; set; }

    /// <summary>
    /// filament Type
    /// </summary>
    public string? FilamentType { get; set; }

    /// <summary>
    /// Detailed filament Type
    /// </summary>
    public string? DetailedFilamentType { get; set; }

    /// <summary>
    /// Color in hex RGBA
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// spool Weight in grams (E8 03 --> 1000 g)
    /// </summary>
    public ushort? SpoolWeight { get; set; }

    /// <summary>
    /// filament Diameter in millimeters
    /// </summary>
    public float? FilamentDiameter { get; set; }

    /// <summary>
    /// Drying Temperature in °C
    /// </summary>
    public ushort? DryingTemperature { get; set; }

    /// <summary>
    /// Drying time in hours
    /// </summary>
    public ushort? DryingTime { get; set; }

    /// <summary>
    /// Bed Temperature Type
    /// </summary>
    public ushort? BedTemperatureType { get; set; }

    /// <summary>
    /// Bed Temperature in °C
    /// </summary>
    public ushort? BedTemperature { get; set; }

    /// <summary>
    /// Max Temperature for Hotend in °C
    /// </summary>
    public ushort? MaxTemperatureForHotend { get; set; }

    /// <summary>
    /// Min Temperature for Hotend in °C
    /// </summary>
    public ushort? MinTemperatureForHotend { get; set; }

    /// <summary>
    /// X Cam info
    /// </summary>
    public byte[]? XCamInfo { get; set; }

    /// <summary>
    /// Nozzle Diameter...?
    /// </summary>
    public float? NozzleDiameter { get; set; }

    /// <summary>
    /// Tray UID
    /// </summary>
    public string? TrayUid { get; set; }

    /// <summary>
    /// spool Width in µm
    /// </summary>
    public ushort? SpoolWidth { get; set; }

    /// <summary>
    /// Production Date and Time
    /// </summary>
    public DateTime? ProductionDateTime { get; set; }

    /// <summary>
    /// Short Production Date/Time...?
    /// </summary>
    public string? ProductionDateTimeShort { get; set; }

    /// <summary>
    /// filament length in meters...?
    /// </summary>
    public ushort? FilamentLength { get; set; }

    /// <summary>
    /// Format Identifier (00 00 = Empty, 02 00 = Color Info)
    /// </summary>
    public ushort? FormatIdentifier { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public ushort? ColorCount { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public string? SecondColor { get; set; }

    /// <summary>
    /// SKU start, missing SPL or SPLFREE
    /// </summary>
    public string? SkuStart { get; set; }

    /// <summary>
    /// Time taken to read nfc tag data
    /// </summary>
    [JsonIgnore]
    public double? ReadTime { get; set; }

    /// <summary>
    /// Byte data read from nfc tag. Only filled when full read is enabled
    /// </summary>
    [JsonIgnore]
    public byte[]? BlockData { get; set; }

    /// <summary>
    /// Keys used to access nfc tag. Only filled when full read is enabled
    /// </summary>
    [JsonIgnore]
    public byte[]? Keys { get; set; }

    public void ParseData(byte[][] blockData, byte[][] keys, bool fillSerial = false, bool fullRead = true)
    {
        if (fullRead)
        {
            BlockData = blockData.SelectMany(x => x).ToArray();
            Keys = keys.SelectMany(x => x).ToArray();
        }

        if (fillSerial) SerialNumber = BitConverter.ToString(blockData[0][..4]).Replace("-", "");

        TagManufacturerData = blockData[0].Length > 4 ? blockData[0][4..] : [];

        if (blockData[1].Length > 8) MaterialVariantIdentifier = Encoding.ASCII.GetString(blockData[1][..8]).Trim('\u0000');
        if (blockData[1].Length >= 16) UniqueMaterialIdentifier = Encoding.ASCII.GetString(blockData[1][9..16]).Trim('\u0000');

        FilamentType = Encoding.ASCII.GetString(blockData[2]).Trim('\u0000');

        DetailedFilamentType = Encoding.ASCII.GetString(blockData[4]).Trim('\u0000');

        Color = BitConverter.ToString(blockData[5][..4]).Replace("-", "");
        SpoolWeight = BitConverter.ToUInt16(blockData[5], 4);
        FilamentDiameter = BitConverter.ToSingle(blockData[5], 8);

        DryingTemperature = BitConverter.ToUInt16(blockData[6], 0);
        DryingTime = BitConverter.ToUInt16(blockData[6], 2);
        BedTemperatureType = BitConverter.ToUInt16(blockData[6], 4);
        BedTemperature = BitConverter.ToUInt16(blockData[6], 6);
        MaxTemperatureForHotend = BitConverter.ToUInt16(blockData[6], 8);
        MinTemperatureForHotend = BitConverter.ToUInt16(blockData[6], 10);

        XCamInfo = blockData[8].Length > 0 ? blockData[8][..12] : [];
        NozzleDiameter = BitConverter.ToSingle(blockData[8], 12);

        TrayUid = BitConverter.ToString(blockData[9]).Replace("-", "");

        SpoolWidth = BitConverter.ToUInt16(blockData[10], 4);

        ProductionDateTime = DateTime.TryParseExact(Encoding.ASCII.GetString(blockData[12]).Trim('\u0000'), "yyyy_MM_dd_HH_mm", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime time) ? time : null;

        ProductionDateTimeShort = Encoding.ASCII.GetString(blockData[13]).Trim('\u0000');

        FilamentLength = BitConverter.ToUInt16(blockData[14], 4);

        FormatIdentifier = BitConverter.ToUInt16(blockData[16], 0);
        ColorCount = BitConverter.ToUInt16(blockData[16], 2);
        SecondColor = string.Join("", BitConverter.ToString(blockData[16][4..8]).Replace("-", "").Reverse());

        SkuStart = $"{MaterialVariantIdentifier}-{$"{FilamentDiameter:0.00}".Replace(",", ".")}-{SpoolWeight:####}";
    }

    public void ParseData(byte[] blockData)
    {
        var data = Enumerable.Range(0, 64).Select(x => blockData[new Range(x * 16, x * 16 + 16)]).ToArray();
        var uidData = blockData[..4];

        var aKeys = uidData.GetBambuAKeys();
        var bKeys = uidData.GetBambuBKeys();
        var keys = aKeys.Concat(bKeys).ToArray();

        #region Fill in keys

        var index = 0;

        for (var i = 3; i < blockData.Length; i += 4)
        {
            data[i] = aKeys[index].Concat(data[i][6..10]).Concat(bKeys[index]).ToArray();
            index++;
        }

        #endregion

        ParseData(data, keys, fullRead: true);
    }
}