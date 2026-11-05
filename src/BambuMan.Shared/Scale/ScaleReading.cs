using BambuMan.Shared.Enums;

namespace BambuMan.Shared.Scale
{
    /// <summary>
    /// One weight sent by a Bluetooth scale, in grams whatever unit the scale used.
    /// </summary>
    /// <param name="Grams">The weight on the scale (g). Can be negative on a scale that was tared with something on it.</param>
    /// <param name="IsStable">The scale has settled. A Weight Scale Service measurement is always final, so always stable.</param>
    /// <param name="Protocol">The protocol the weight came in.</param>
    /// <param name="ReceivedAt">When the phone received it (UTC), which is what freshness is judged by.</param>
    public record ScaleReading(decimal Grams, bool IsStable, ScaleProtocol Protocol, DateTime ReceivedAt)
    {
        /// <summary>Decodes a characteristic value of <paramref name="protocol"/>; false when it carries no valid weight.</summary>
        public static bool TryDecode(ScaleProtocol protocol, ReadOnlySpan<byte> data, DateTime receivedAt, out ScaleReading? reading)
        {
            reading = null;

            switch (protocol)
            {
                case ScaleProtocol.WeightScaleService:
                    if (!WeightMeasurementDecoder.TryDecode(data, out var measurement)) return false;

                    reading = new ScaleReading(measurement!.Grams, true, protocol, receivedAt);
                    return true;

                case ScaleProtocol.BambuMan:
                    if (!BambuManScaleDecoder.TryDecode(data, out var grams, out var stable)) return false;

                    reading = new ScaleReading(grams, stable, protocol, receivedAt);
                    return true;

                default:
                    return false;
            }
        }
    }
}
