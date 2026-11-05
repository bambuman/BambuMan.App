using BambuMan.Shared.Enums;
using BambuMan.Shared.Scale;

namespace BambuMan.Shared.Test.Scale
{
    [Trait("Category", "Scale")]
    public class ScaleDecoderTests
    {
        private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

        #region Weight Measurement (0x2A9D)

        [Fact(DisplayName = "SI weight is read in steps of 0.005 kg")]
        public void WeightMeasurement_Si()
        {
            // 0x2ED4 = 11988 * 0.005 kg = 59.94 kg
            Assert.True(WeightMeasurementDecoder.TryDecode([0x00, 0xD4, 0x2E], out var measurement));

            Assert.Equal(WeightUnit.Kilogram, measurement!.Unit);
            Assert.Equal(59.94m, measurement.Weight);
            Assert.Equal(59940m, measurement.Grams);
            Assert.Null(measurement.Timestamp);
            Assert.Null(measurement.UserId);
            Assert.Null(measurement.Bmi);
            Assert.Null(measurement.Height);
        }

        [Fact(DisplayName = "Imperial weight is read in steps of 0.01 lb and converted to grams")]
        public void WeightMeasurement_Imperial()
        {
            // 0x0EA6 = 3750 * 0.01 lb = 37.5 lb
            Assert.True(WeightMeasurementDecoder.TryDecode([0x01, 0xA6, 0x0E], out var measurement));

            Assert.Equal(WeightUnit.Pound, measurement!.Unit);
            Assert.Equal(37.5m, measurement.Weight);
            Assert.Equal(37.5m * 453.59237m, measurement.Grams);
        }

        [Fact(DisplayName = "Time stamp, user id, BMI and height are read when their flags are set")]
        public void WeightMeasurement_AllOptionalFields()
        {
            byte[] data =
            [
                0x0E,                                     // SI, time stamp, user id, BMI + height
                0x70, 0x17,                               // 6000 * 0.005 = 30 kg
                0xEA, 0x07, 0x0B, 0x05, 0x0C, 0x22, 0x38, // 2026-11-05 12:34:56
                0x03,                                     // user 3
                0xE1, 0x00,                               // BMI 22.5
                0xB8, 0x06                                // 1720 * 0.001 = 1.72 m
            ];

            Assert.True(WeightMeasurementDecoder.TryDecode(data, out var measurement));

            Assert.Equal(30m, measurement!.Weight);
            Assert.Equal(new DateTime(2026, 11, 5, 12, 34, 56), measurement.Timestamp);
            Assert.Equal((byte)3, measurement.UserId);
            Assert.Equal(22.5m, measurement.Bmi);
            Assert.Equal(1.72m, measurement.Height);
        }

        [Fact(DisplayName = "Imperial height is read in steps of 0.1 inch")]
        public void WeightMeasurement_ImperialHeight()
        {
            // imperial + BMI/height: 100 lb, BMI 20.0, height 0x02B2 = 690 * 0.1 = 69 in
            Assert.True(WeightMeasurementDecoder.TryDecode([0x09, 0x10, 0x27, 0xC8, 0x00, 0xB2, 0x02], out var measurement));

            Assert.Equal(100m, measurement!.Weight);
            Assert.Equal(20m, measurement.Bmi);
            Assert.Equal(69m, measurement.Height);
        }

        [Fact(DisplayName = "A time stamp the scale doesn't know (zeroes) reads as none, the weight is still used")]
        public void WeightMeasurement_UnknownTimestamp()
        {
            Assert.True(WeightMeasurementDecoder.TryDecode([0x02, 0x70, 0x17, 0, 0, 0, 0, 0, 0, 0], out var measurement));

            Assert.Equal(30m, measurement!.Weight);
            Assert.Null(measurement.Timestamp);
        }

        [Fact(DisplayName = "An impossible date reads as no time stamp")]
        public void WeightMeasurement_InvalidTimestamp()
        {
            // 2026-02-30
            Assert.True(WeightMeasurementDecoder.TryDecode([0x02, 0x70, 0x17, 0xEA, 0x07, 0x02, 0x1E, 0, 0, 0], out var measurement));

            Assert.Null(measurement!.Timestamp);
        }

        [Fact(DisplayName = "A failed measurement (0xFFFF) is no weight")]
        public void WeightMeasurement_Unsuccessful()
        {
            Assert.False(WeightMeasurementDecoder.TryDecode([0x00, 0xFF, 0xFF], out var measurement));
            Assert.Null(measurement);
        }

        [Theory(DisplayName = "A value shorter than its flags announce is rejected")]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 0x00 })]
        [InlineData(new byte[] { 0x00, 0x70 })]
        [InlineData(new byte[] { 0x02, 0x70, 0x17, 0xEA, 0x07, 0x0B })]
        [InlineData(new byte[] { 0x04, 0x70, 0x17 })]
        [InlineData(new byte[] { 0x08, 0x70, 0x17, 0xE1, 0x00, 0xB8 })]
        public void WeightMeasurement_TooShort(byte[] data)
        {
            Assert.False(WeightMeasurementDecoder.TryDecode(data, out _));
        }

        [Fact(DisplayName = "A Weight Scale Service measurement is a stable reading")]
        public void WeightMeasurement_ReadingIsStable()
        {
            Assert.True(ScaleReading.TryDecode(ScaleProtocol.WeightScaleService, [0x00, 0xE8, 0x03], Now, out var reading));

            // 1000 * 0.005 kg = 5 kg
            Assert.Equal(new ScaleReading(5000m, true, ScaleProtocol.WeightScaleService, Now), reading);
        }

        #endregion

        #region BambuMan scale protocol

        [Fact(DisplayName = "BambuMan weight is int32 little-endian in 0.1 g, with the stable flag")]
        public void BambuMan_Stable()
        {
            // 12345 * 0.1 g = 1234.5 g
            Assert.True(BambuManScaleDecoder.TryDecode([0x39, 0x30, 0x00, 0x00, 0x01], out var grams, out var stable));

            Assert.Equal(1234.5m, grams);
            Assert.True(stable);
        }

        [Fact(DisplayName = "BambuMan weight without the stable flag is still settling")]
        public void BambuMan_Live()
        {
            Assert.True(ScaleReading.TryDecode(ScaleProtocol.BambuMan, [0x39, 0x30, 0x00, 0x00, 0x00], Now, out var reading));

            Assert.Equal(new ScaleReading(1234.5m, false, ScaleProtocol.BambuMan, Now), reading);
        }

        [Fact(DisplayName = "BambuMan weight can be negative after a tare")]
        public void BambuMan_Negative()
        {
            // -25 = 0xFFFFFFE7 -> -2.5 g
            Assert.True(BambuManScaleDecoder.TryDecode([0xE7, 0xFF, 0xFF, 0xFF, 0x01], out var grams, out _));

            Assert.Equal(-2.5m, grams);
        }

        [Fact(DisplayName = "BambuMan weight flagged as an error is rejected")]
        public void BambuMan_Error()
        {
            Assert.False(BambuManScaleDecoder.TryDecode([0x39, 0x30, 0x00, 0x00, 0x03], out _, out _));
        }

        [Fact(DisplayName = "BambuMan bytes after the flags are reserved and ignored")]
        public void BambuMan_ExtraBytesIgnored()
        {
            Assert.True(BambuManScaleDecoder.TryDecode([0x10, 0x27, 0x00, 0x00, 0x01, 0xAA, 0xBB], out var grams, out var stable));

            Assert.Equal(1000m, grams);
            Assert.True(stable);
        }

        [Theory(DisplayName = "BambuMan values shorter than 5 bytes are rejected")]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 0x39, 0x30, 0x00, 0x00 })]
        public void BambuMan_TooShort(byte[] data)
        {
            Assert.False(ScaleReading.TryDecode(ScaleProtocol.BambuMan, data, Now, out var reading));
            Assert.Null(reading);
        }

        #endregion

        #region Uuids

        [Fact(DisplayName = "16-bit SIG numbers expand on the Bluetooth base uuid")]
        public void Uuids_FromShort()
        {
            Assert.Equal(Guid.Parse("0000181d-0000-1000-8000-00805f9b34fb"), ScaleUuids.WeightScaleService);
            Assert.Equal(Guid.Parse("00002a9d-0000-1000-8000-00805f9b34fb"), ScaleUuids.WeightMeasurement);
        }

        #endregion
    }
}
