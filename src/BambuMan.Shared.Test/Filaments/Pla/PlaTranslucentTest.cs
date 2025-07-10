﻿namespace BambuMan.Shared.Test.Filaments.Pla
{
    [Trait("Category", "PLA Translucent")]
    public class PlaTranslucentTest : BaseTest
    {
        [Fact(DisplayName = "Translucent Cherry Pink")]
        public async Task TranslucentCherryPink()
        {
            var json = "{\"SerialNumber\":\"83EC9A1C\",\"TagManufacturerData\":\"6QgEAAR8EZ2x4zmQ\",\"MaterialVariantIdentifier\":\"A17-R1\",\"UniqueMaterialIdentifier\":\"FA17\",\"FilamentType\":\"PLA\",\"DetailedFilamentType\":\"PLA Translucent\",\"Color\":\"F5B6CD80\",\"SpoolWeight\":1000,\"FilamentDiameter\":1.75,\"DryingTemperature\":55,\"DryingTime\":8,\"BedTemperatureType\":0,\"BedTemperature\":0,\"MaxTemperatureForHotend\":240,\"MinTemperatureForHotend\":200,\"XCamInfo\":\"AAAAAAAAAAAAAAAA\",\"NozzleDiameter\":0.2,\"TrayUid\":\"2DC9E553D1924FA89FBB893C9E921DBA\",\"SpoolWidth\":666,\"ProductionDateTime\":\"2024-12-20T09:40:00\",\"ProductionDateTimeShort\":\"20241220\",\"FilamentLength\":345,\"FormatIdentifier\":2,\"ColorCount\":1,\"SecondColor\":\"00000000\",\"SkuStart\":\"A17-R1-1.75-1000\"}";

            var (_, external) = await GetExternalFilament(json);
            
            Assert.Equal("Translucent Cherry Pink", external?.Name);
            Assert.Equal("PLA", external?.Material);
        }
    }
}
