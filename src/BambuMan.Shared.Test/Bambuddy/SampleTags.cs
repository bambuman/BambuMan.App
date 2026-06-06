namespace BambuMan.Shared.Test.Bambuddy
{
    /// <summary>Real Bambu Lab NFC tag payloads (as <c>BambuFilamentInfo</c> JSON) reused across Bambuddy tests.</summary>
    internal static class SampleTags
    {
        // ABS White — 1000 g spool, color FFFFFFFF, nozzle 240-270.
        public const string AbsWhite =
            """
            {"SerialNumber":"3A0258BD","TagManufacturerData":"3QgEAARiU+WY16OQ","MaterialVariantIdentifier":"B00-W0","UniqueMaterialIdentifier":"FB00","FilamentType":"ABS","DetailedFilamentType":"ABS","Color":"FFFFFFFF","SpoolWeight":1000,"FilamentDiameter":1.75,"DryingTemperature":80,"DryingTime":8,"BedTemperatureType":0,"BedTemperature":0,"MaxTemperatureForHotend":270,"MinTemperatureForHotend":240,"XCamInfo":"0AfQB+gD6APNzEw/","NozzleDiameter":0.2,"TrayUid":"658AF4C881A64A0781B32CFB5A7CB675","SpoolWidth":6625,"ProductionDateTime":"2024-06-03T13:24:00","ProductionDateTimeShort":"24_06_03_13","FilamentLength":398,"FormatIdentifier":2,"ColorCount":1,"SecondColor":"00000000","SkuStart":"B00-W0-1.75-1000"}
            """;

        // ABS Black — 1000 g spool, color 000000FF.
        public const string AbsBlack =
            """
            {"SerialNumber":"94B0C6D5","TagManufacturerData":"NwgEAATFX3/GYsCQ","MaterialVariantIdentifier":"B00-K0","UniqueMaterialIdentifier":"FB00","FilamentType":"ABS","DetailedFilamentType":"ABS","Color":"000000FF","SpoolWeight":1000,"FilamentDiameter":1.75,"DryingTemperature":80,"DryingTime":8,"BedTemperatureType":0,"BedTemperature":0,"MaxTemperatureForHotend":270,"MinTemperatureForHotend":240,"XCamInfo":"gD4QJ+gD6AMzMzM/","NozzleDiameter":0.2,"TrayUid":"5E755D3AE0FD409F913D5A89F817A248","SpoolWidth":6625,"ProductionDateTime":"2024-11-27T09:19:00","ProductionDateTimeShort":"24_11_27_09","FilamentLength":398,"FormatIdentifier":2,"ColorCount":1,"SecondColor":"00000000","SkuStart":"B00-K0-1.75-1000"}
            """;
    }
}
