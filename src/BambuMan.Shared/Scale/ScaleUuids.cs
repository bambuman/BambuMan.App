namespace BambuMan.Shared.Scale
{
    /// <summary>The GATT services and characteristics a Bluetooth scale is found and read by.</summary>
    public static class ScaleUuids
    {
        #region BambuMan scale protocol (docs/scale-protocol.md)

        /// <summary>The BambuMan scale service, advertised by the scale so a scan finds it.</summary>
        public static readonly Guid BambuManService = Guid.Parse("d9a40001-3bc4-40b0-839e-ca2ccb948710");

        /// <summary>Notify: int32 weight in 0.1 g + flags byte, see <see cref="BambuManScaleDecoder"/>.</summary>
        public static readonly Guid BambuManWeight = Guid.Parse("d9a40002-3bc4-40b0-839e-ca2ccb948710");

        /// <summary>Optional, write: one command byte, <see cref="BambuManScaleDecoder.TareCommand"/> zeroes the scale.</summary>
        public static readonly Guid BambuManControl = Guid.Parse("d9a40003-3bc4-40b0-839e-ca2ccb948710");

        #endregion

        #region Bluetooth SIG Weight Scale Service

        /// <summary>Weight Scale Service, 0x181D.</summary>
        public static readonly Guid WeightScaleService = FromShort(0x181D);

        /// <summary>Weight Measurement, 0x2A9D (indicate), see <see cref="WeightMeasurementDecoder"/>.</summary>
        public static readonly Guid WeightMeasurement = FromShort(0x2A9D);

        #endregion

        /// <summary>Every service a scale is scanned for.</summary>
        public static Guid[] ScaleServices => [BambuManService, WeightScaleService];

        /// <summary>A 16-bit SIG-assigned number on the Bluetooth base UUID (0000xxxx-0000-1000-8000-00805f9b34fb).</summary>
        public static Guid FromShort(ushort id) => Guid.Parse($"0000{id:x4}-0000-1000-8000-00805f9b34fb");
    }
}
