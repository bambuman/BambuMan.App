﻿namespace BambuMan.Shared.Test.Filaments.Pla
{
    [Trait("Category", "PLA Metal")]
    public class PlaMetalTest : BaseTest
    {
        [Fact(DisplayName = "Cobalt Blue Metallic")]
        public async Task CobaltBlueMetallic()
        {
            var json = "{\"SerialNumber\":\"5502230D\",\"TagManufacturerData\":\"eQgEAAQisrA8BTiQ\",\"MaterialVariantIdentifier\":\"A02-B2\",\"UniqueMaterialIdentifier\":\"FA02\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Metal\",\"Color\":\"39699EFF\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":230,\"MinTemperatureForHotend\":190,\"XCamInfo\":\"0AesDegD6APNzEw/\",\"NozzleDiameter\":0.2,\"TrayUid\":\"A356B475EE364773B6A6A78696C46E34\",\"SpoolWidth\":6625,\"ProductionDateTime\":\"2025-03-13T15:36:00\",\"ProductionDateTimeShort\":\"25_03_13_15\",\"FilamentLength\":330,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A02-B2-1.75-1000\"}";

            var (_, external) = await GetExternalFilament(json);
            
            Assert.Equal("Cobalt Blue Metallic", external?.Name);
            Assert.Equal("PLA", external?.Material);
        }
    }
}
