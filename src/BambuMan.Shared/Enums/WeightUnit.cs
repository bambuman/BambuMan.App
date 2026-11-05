namespace BambuMan.Shared.Enums
{
    /// <summary>The unit a Weight Measurement (0x2A9D) is sent in, from bit 0 of its flags.</summary>
    public enum WeightUnit
    {
        /// <summary>SI: kilograms, in steps of 0.005 kg.</summary>
        Kilogram,

        /// <summary>Imperial: pounds, in steps of 0.01 lb.</summary>
        Pound
    }
}
