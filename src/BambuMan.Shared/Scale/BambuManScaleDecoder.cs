using System.Buffers.Binary;

namespace BambuMan.Shared.Scale
{
    /// <summary>
    /// The BambuMan scale protocol (docs/scale-protocol.md), for DIY scales. The weight characteristic
    /// (<see cref="ScaleUuids.BambuManWeight"/>) notifies at least 5 bytes:
    /// <list type="bullet">
    /// <item>bytes 0–3: weight, int32 little-endian, in 0.1 g</item>
    /// <item>byte 4: flags, bit 0 stable, bit 1 error (overload, sensor fault — the weight is not valid)</item>
    /// </list>
    /// Bytes after these are reserved and ignored, so the format can grow without breaking older apps.
    /// </summary>
    public static class BambuManScaleDecoder
    {
        #region Flags

        public const byte FlagStable = 0x01;
        public const byte FlagError = 0x02;

        #endregion

        public const int MinimumLength = 5;

        public const decimal Resolution = 0.1m;

        /// <summary>Written to the control characteristic (<see cref="ScaleUuids.BambuManControl"/>) to zero the scale.</summary>
        public const byte TareCommand = 0x01;

        /// <summary>False when the value is too short or the scale flags the weight as an error.</summary>
        public static bool TryDecode(ReadOnlySpan<byte> data, out decimal grams, out bool stable)
        {
            grams = 0;
            stable = false;

            if (data.Length < MinimumLength) return false;

            var flags = data[4];
            if ((flags & FlagError) != 0) return false;

            grams = BinaryPrimitives.ReadInt32LittleEndian(data) * Resolution;
            stable = (flags & FlagStable) != 0;
            return true;
        }
    }
}
