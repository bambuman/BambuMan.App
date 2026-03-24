using Newtonsoft.Json;
using SpoolMan.Api.Model;

namespace BambuMan.Shared.Test
{
    public abstract class BaseTest
    {
        internal SpoolmanManager SpoolmanManager { get; } = new SpoolmanManager(null).Test();

        internal async Task<(BambuFilamentInfo?, ExternalFilament?)> GetExternalFilament(string json)
        {
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(json);

            var external = info != null ? await SpoolmanManager.FindExternalFilament(info) : null;

            Assert.True(info != null, "Can't deserialize json to BambuFilamentInfo");
            Assert.True(external != null, "Can't find external filament");

            return (info, external);
        }
    }
}
