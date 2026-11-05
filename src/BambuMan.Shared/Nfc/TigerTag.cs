using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Newtonsoft.Json;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace BambuMan.Shared.Nfc
{
    /// <summary>
    /// The TigerTag format (TigerTag-Project/TigerTag-RFID-Guide, protocol v2.2): a fixed big-endian binary layout in
    /// the user pages 0x04–0x27 of an NTAG21x, not NDEF. Material, brand, aspect, diameter and unit are ids, resolved
    /// against the reference tables shipped with the protocol (CC0), embedded here as <c>Resources/tigertag.json</c>.
    /// Only the 80 data bytes (pages 0x04–0x17) are read; the optional ECDSA signature (0x18–0x27) is not verified.
    /// </summary>
    public static class TigerTag
    {
        /// <summary>First NTAG page of the payload.</summary>
        public const int FirstPage = 0x04;

        /// <summary>Data bytes before the signature, pages 0x04–0x17.</summary>
        public const int DataLength = 80;

        /// <summary>Bytes needed for every field this parser reads except the message and remaining quantity.</summary>
        private const int MinimumLength = 48;

        public const uint IdTigerTag = 0x5BF59264;
        public const uint IdTigerTagPlus = 0xBC0FCB97;
        public const uint IdTigerTagInit = 0x6C41A2E1;

        /// <summary><c>ID Type</c> of a filament spool (the format also covers resin and accessories).</summary>
        public const byte TypeFilament = 0x8E;

        /// <summary><c>ID Product</c> of a standard (non-catalogue) TigerTag.</summary>
        private const uint NoProduct = 0xFFFFFFFF;

        private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        #region Read

        /// <summary>True when <paramref name="userMemory"/> (starting at page 0x04) starts with a TigerTag / TigerTag+ id.</summary>
        public static bool IsTigerTag(byte[]? userMemory) =>
            userMemory is { Length: >= 4 } && BinaryPrimitives.ReadUInt32BigEndian(userMemory) is IdTigerTag or IdTigerTagPlus;

        /// <summary>
        /// Parse the user memory of a TigerTag, read from page 0x04 on. Null when it isn't one, is only initialised
        /// (TigerTag Init, no material yet), or describes something other than filament.
        /// </summary>
        public static SpoolTag? TryParse(byte[] identifier, byte[]? userMemory)
        {
            if (userMemory == null || userMemory.Length < MinimumLength || !IsTigerTag(userMemory)) return null;

            var data = userMemory.AsSpan();

            if (data[12] != TypeFilament) return null;

            var tables = Tables.Value;

            var productId = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
            var material = tables.Material(BinaryPrimitives.ReadUInt16BigEndian(data[8..]));
            var aspect1 = tables.Aspect(data[10]);
            var aspect2 = tables.Aspect(data[11]);
            var diameter = tables.Label(tables.Diameters, data[13]);
            var brand = tables.Brand(BinaryPrimitives.ReadUInt16BigEndian(data[14..]));
            var unit = tables.Label(tables.Units, data[23]);
            var measure = ReadUInt24(data[20..]);
            var timestamp = BinaryPrimitives.ReadUInt32BigEndian(data[32..]);

            // Aspect 2 carries the colour composition (Bicolor, Tricolor, Rainbow) when there is one — check it first.
            var colorCount = Math.Max(aspect2?.ColorCount ?? 0, aspect1?.ColorCount ?? 0);
            var additionalColors = new List<string>();
            if (colorCount >= 2) additionalColors.Add(Convert.ToHexString(data.Slice(36, 3)) + "FF");
            if (colorCount >= 3) additionalColors.Add(Convert.ToHexString(data.Slice(40, 3)) + "FF");

            var finish = string.Join(" ", new[] { aspect1, aspect2 }.Where(IsFinish).Select(x => x!.Label));
            var message = data.Length >= 76 ? Encoding.UTF8.GetString(data.Slice(48, 28)).TrimEnd('\0', ' ') : string.Empty;

            return new SpoolTag(SpoolTagFormat.TigerTag, identifier)
            {
                Brand = brand,
                FilamentType = material?.Label,
                Subtype = finish.Length > 0 ? finish : null,
                Color = Convert.ToHexString(data.Slice(16, 4)),
                AdditionalColors = additionalColors,
                SpoolWeight = Grams(measure, unit),
                FilamentDiameter = float.TryParse(diameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) ? mm : null,
                MinTemperatureForHotend = NonZero(BinaryPrimitives.ReadUInt16BigEndian(data[24..])),
                MaxTemperatureForHotend = NonZero(BinaryPrimitives.ReadUInt16BigEndian(data[26..])),
                DryingTemperature = NonZero(data[28]),
                DryingTime = NonZero(data[29]),
                BedTemperature = NonZero(data[30]),
                ProductionDateTime = timestamp == 0 ? null : Epoch.AddSeconds(timestamp),
                ProductId = productId == NoProduct ? null : productId,
                Message = message.Length > 0 ? message : null,
                RemainingWeight = data.Length >= DataLength ? Grams(ReadUInt24(data[76..]), unit) : null,
                RawPayload = Convert.ToHexString(data[..Math.Min(DataLength, data.Length)])
            };
        }

        #endregion

        #region Helpers

        private static uint ReadUInt24(ReadOnlySpan<byte> data) => (uint)(data[0] << 16 | data[1] << 8 | data[2]);

        private static ushort? NonZero(int value) => value == 0 ? null : (ushort)value;

        /// <summary>A weight in grams from a quantity and its unit; null for volume units (resin) or a value that doesn't fit.</summary>
        private static ushort? Grams(uint measure, string? unit)
        {
            if (measure == 0) return null;

            var grams = unit switch
            {
                "g" => measure,
                "kg" => measure * 1000m,
                "mg" => measure / 1000m,
                _ => (decimal?)null
            };

            return grams is > 0 and <= ushort.MaxValue ? (ushort)Math.Round(grams.Value) : null;
        }

        /// <summary>An aspect that describes a finish (Silk, Matt, …) rather than nothing or a colour composition.</summary>
        private static bool IsFinish(TigerTagAspect? aspect) => aspect is { ColorCount: 1 };

        #endregion

        #region Reference tables

        private static readonly Lazy<TigerTagTables> Tables = new(LoadTables);

        private static TigerTagTables LoadTables()
        {
            using var stream = typeof(TigerTag).Assembly.GetManifestResourceStream("BambuMan.Shared.Resources.tigertag.json")
                ?? throw new FileNotFoundException("Embedded resource not found");

            using var reader = new StreamReader(stream);

            return JsonConvert.DeserializeObject<TigerTagTables>(reader.ReadToEnd()) ?? new TigerTagTables();
        }

        #endregion
    }

    #region Reference table models

    internal class TigerTagTables
    {
        public List<TigerTagMaterial> Materials { get; set; } = [];
        public List<TigerTagBrand> Brands { get; set; } = [];
        public List<TigerTagAspect> Aspects { get; set; } = [];
        public List<TigerTagLabel> Diameters { get; set; } = [];
        public List<TigerTagLabel> Units { get; set; } = [];

        public TigerTagMaterial? Material(int id) => id == 0 ? null : Materials.FirstOrDefault(x => x.Id == id);

        public string? Brand(int id) => id == 0 ? null : Brands.FirstOrDefault(x => x.Id == id)?.Name;

        public string? Label(IEnumerable<TigerTagLabel> table, int id) => table.FirstOrDefault(x => x.Id == id)?.Label;

        public TigerTagAspect? Aspect(int id) => Aspects.FirstOrDefault(x => x.Id == id);
    }

    internal record TigerTagMaterial(int Id, string Label, string? MaterialType, decimal? Density);

    internal record TigerTagBrand(int Id, string Name);

    internal record TigerTagAspect(int Id, string Label, int ColorCount);

    internal record TigerTagLabel(int Id, string Label);

    #endregion
}
