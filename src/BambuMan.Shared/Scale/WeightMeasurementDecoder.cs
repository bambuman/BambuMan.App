using BambuMan.Shared.Enums;
using System.Buffers.Binary;

namespace BambuMan.Shared.Scale
{
    /// <summary>A decoded Weight Measurement (0x2A9D) of the Bluetooth SIG Weight Scale Service.</summary>
    /// <param name="Unit">The unit <paramref name="Weight"/> and <paramref name="Height"/> are in.</param>
    /// <param name="Weight">Kilograms or pounds, by <paramref name="Unit"/>.</param>
    /// <param name="Timestamp">The scale's own clock, when present and valid.</param>
    /// <param name="UserId">The scale's user index, when present (0xFF is an unknown user).</param>
    /// <param name="Bmi">Body mass index, when present.</param>
    /// <param name="Height">Metres or inches, by <paramref name="Unit"/>, when present.</param>
    public record WeightMeasurement(WeightUnit Unit, decimal Weight, DateTime? Timestamp, byte? UserId, decimal? Bmi, decimal? Height)
    {
        public decimal Grams => ScaleWeight.ToGrams(Weight, Unit);
    }

    /// <summary>
    /// Decodes the Weight Measurement characteristic (0x2A9D), little-endian:
    /// <list type="bullet">
    /// <item>flags (uint8): bit 0 unit (0 SI, 1 imperial), bit 1 time stamp present, bit 2 user id present, bit 3 BMI and height present</item>
    /// <item>weight (uint16): 0.005 kg or 0.01 lb; 0xFFFF is "measurement unsuccessful"</item>
    /// <item>time stamp (7 bytes, optional): year (uint16), month, day, hours, minutes, seconds</item>
    /// <item>user id (uint8, optional)</item>
    /// <item>BMI (uint16, 0.1) and height (uint16, 0.001 m or 0.1 in) (optional)</item>
    /// </list>
    /// </summary>
    public static class WeightMeasurementDecoder
    {
        #region Flags

        private const byte FlagImperial = 0x01;
        private const byte FlagTimestamp = 0x02;
        private const byte FlagUserId = 0x04;
        private const byte FlagBmiAndHeight = 0x08;

        #endregion

        public const ushort MeasurementUnsuccessful = 0xFFFF;

        public const decimal KilogramResolution = 0.005m;
        public const decimal PoundResolution = 0.01m;

        /// <summary>False when the value is too short for the fields its flags announce, or the measurement failed.</summary>
        public static bool TryDecode(ReadOnlySpan<byte> data, out WeightMeasurement? measurement)
        {
            measurement = null;

            if (data.Length < 3) return false;

            var flags = data[0];
            var imperial = (flags & FlagImperial) != 0;
            var hasTimestamp = (flags & FlagTimestamp) != 0;
            var hasUserId = (flags & FlagUserId) != 0;
            var hasBmiAndHeight = (flags & FlagBmiAndHeight) != 0;

            var length = 3 + (hasTimestamp ? 7 : 0) + (hasUserId ? 1 : 0) + (hasBmiAndHeight ? 4 : 0);
            if (data.Length < length) return false;

            var rawWeight = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
            if (rawWeight == MeasurementUnsuccessful) return false;

            var offset = 3;

            DateTime? timestamp = null;
            if (hasTimestamp)
            {
                timestamp = ReadDateTime(data.Slice(offset, 7));
                offset += 7;
            }

            byte? userId = null;
            if (hasUserId)
            {
                userId = data[offset];
                offset += 1;
            }

            decimal? bmi = null;
            decimal? height = null;
            if (hasBmiAndHeight)
            {
                bmi = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]) * 0.1m;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]) * (imperial ? 0.1m : 0.001m);
            }

            var unit = imperial ? WeightUnit.Pound : WeightUnit.Kilogram;
            var weight = rawWeight * (imperial ? PoundResolution : KilogramResolution);

            measurement = new WeightMeasurement(unit, weight, timestamp, userId, bmi, height);
            return true;
        }

        /// <summary>The Bluetooth Date Time format; null when a part is "not known" (0) or out of range.</summary>
        private static DateTime? ReadDateTime(ReadOnlySpan<byte> data)
        {
            var year = BinaryPrimitives.ReadUInt16LittleEndian(data);
            int month = data[2], day = data[3], hours = data[4], minutes = data[5], seconds = data[6];

            if (year is < 1582 or > 9999 || month is < 1 or > 12) return null;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
            if (hours > 23 || minutes > 59 || seconds > 59) return null;

            return new DateTime(year, month, day, hours, minutes, seconds, DateTimeKind.Unspecified);
        }
    }
}
