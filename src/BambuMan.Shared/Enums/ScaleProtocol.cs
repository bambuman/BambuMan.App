using BambuMan.Shared.Resources.Strings;

namespace BambuMan.Shared.Enums
{
    /// <summary>The GATT protocol a Bluetooth scale speaks. Persisted by name, so this order is safe to change.</summary>
    public enum ScaleProtocol
    {
        /// <summary>The DIY scale protocol documented in docs/scale-protocol.md (ESP32 + HX711 and similar).</summary>
        BambuMan,

        /// <summary>The Bluetooth SIG Weight Scale Service (0x181D), mostly used by body scales.</summary>
        WeightScaleService
    }

    public static class ScaleProtocolExtensions
    {
        /// <summary>How the protocol is labelled in the ui.</summary>
        public static string DisplayName(this ScaleProtocol protocol) => protocol switch
        {
            ScaleProtocol.WeightScaleService => "Weight Scale Service",
            _ => SharedResources.Scale_ProtocolBambuMan
        };
    }
}
