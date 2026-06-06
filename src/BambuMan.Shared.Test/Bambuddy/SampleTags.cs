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

        // PLA Wood (Classic Birch) — material PLA, detailed "PLA Wood" (subtype strips to "Wood"), code FA16 -> GFA16.
        public const string PlaWood =
            """
            {"SerialNumber":"5B1449F6","TagManufacturerData":"8AgEAATrOVf5DLaQ","MaterialVariantIdentifier":"A16-G0","UniqueMaterialIdentifier":"FA16","FilamentType":"PLA","DetailedFilamentType":"PLA Wood","Color":"918669FF","SpoolWeight":1000,"FilamentDiameter":1.75,"DryingTemperature":60,"DryingTime":6,"BedTemperatureType":0,"BedTemperature":0,"MaxTemperatureForHotend":230,"MinTemperatureForHotend":190,"XCamInfo":"AAAAAAAAAAAAAAAA","NozzleDiameter":0.2,"TrayUid":"4663E9ADF9CC454380EB58CE627BFE72","SpoolWidth":1536,"ProductionDateTime":"2025-03-11T00:38:00","ProductionDateTimeShort":"25_03_11_00","FilamentLength":330,"FormatIdentifier":2,"ColorCount":1,"SecondColor":"00000000","SkuStart":"A16-G0-1.75-1000"}
            """;

        // ASA Aero (White) — FilamentType "ASA Aero"; normalized to Material "ASA", no subtype, code FB02 -> GFB02.
        public const string AsaAero =
            """
            {"SerialNumber":"736E63A1","TagManufacturerData":"3wgEAATV1GncjY2Q","MaterialVariantIdentifier":"B02-W0","UniqueMaterialIdentifier":"FB02","FilamentType":"ASA Aero","DetailedFilamentType":"ASA Aero","Color":"E9E4D9FF","SpoolWeight":1000,"FilamentDiameter":1.75,"DryingTemperature":80,"DryingTime":8,"BedTemperatureType":0,"BedTemperature":0,"MaxTemperatureForHotend":280,"MinTemperatureForHotend":240,"XCamInfo":"ECcQJ+gD6AMAAIA/","NozzleDiameter":0.2,"TrayUid":"21C22A1A7CFD412E8F7CF65955801DD3","SpoolWidth":3717,"ProductionDateTime":"2025-01-03T13:56:00","ProductionDateTimeShort":"25_01_03_13","FilamentLength":420,"FormatIdentifier":2,"ColorCount":1,"SecondColor":"00000000","SkuStart":"B02-W0-1.75-1000"}
            """;

        // ABS Black — 1000 g spool, color 000000FF.
        public const string AbsBlack =
            """
            {"SerialNumber":"94B0C6D5","TagManufacturerData":"NwgEAATFX3/GYsCQ","MaterialVariantIdentifier":"B00-K0","UniqueMaterialIdentifier":"FB00","FilamentType":"ABS","DetailedFilamentType":"ABS","Color":"000000FF","SpoolWeight":1000,"FilamentDiameter":1.75,"DryingTemperature":80,"DryingTime":8,"BedTemperatureType":0,"BedTemperature":0,"MaxTemperatureForHotend":270,"MinTemperatureForHotend":240,"XCamInfo":"gD4QJ+gD6AMzMzM/","NozzleDiameter":0.2,"TrayUid":"5E755D3AE0FD409F913D5A89F817A248","SpoolWidth":6625,"ProductionDateTime":"2024-11-27T09:19:00","ProductionDateTimeShort":"24_11_27_09","FilamentLength":398,"FormatIdentifier":2,"ColorCount":1,"SecondColor":"00000000","SkuStart":"B00-K0-1.75-1000"}
            """;
    }
}
