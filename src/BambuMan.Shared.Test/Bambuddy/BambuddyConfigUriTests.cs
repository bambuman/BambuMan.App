using BambuMan.Shared;

namespace BambuMan.Shared.Test.Bambuddy
{
    [Trait("Category", "Bambuddy")]
    public class BambuddyConfigUriTests
    {
        [Fact(DisplayName = "TryParse extracts URL-decoded url + key from a config URI")]
        public void TryParse_ExtractsUrlAndKey()
        {
            const string uri = "bambuddy://config?v=1&url=http%3A%2F%2F10.10.10.126%3A8000&key=bb_abc123";

            Assert.True(BambuddyConfigUri.TryParse(uri, out var url, out var key));
            Assert.Equal("http://10.10.10.126:8000", url);
            Assert.Equal("bb_abc123", key);
        }

        [Fact(DisplayName = "Build and TryParse round-trip url + key, including special characters")]
        public void Build_TryParse_RoundTrips()
        {
            const string url = "https://bambuddy.example.com:8443";
            const string key = "bb_a+b/c=d e"; // +, /, =, space must survive encode -> decode

            var built = BambuddyConfigUri.Build(url, key);

            Assert.True(BambuddyConfigUri.TryParse(built, out var parsedUrl, out var parsedKey));
            Assert.Equal(url, parsedUrl);
            Assert.Equal(key, parsedKey);
        }

        [Theory(DisplayName = "IsConfigUri matches the bambuddy scheme only")]
        [InlineData("bambuddy://config?v=1&url=x&key=y", true)]
        [InlineData("BAMBUDDY://config?url=x", true)]
        [InlineData("http://10.10.10.126:8000", false)]
        [InlineData("bb_abc123", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsConfigUri_MatchesSchemeOnly(string? value, bool expected) =>
            Assert.Equal(expected, BambuddyConfigUri.IsConfigUri(value));

        [Fact(DisplayName = "TryParse rejects a plain URL or a plain key")]
        public void TryParse_RejectsNonConfig()
        {
            Assert.False(BambuddyConfigUri.TryParse("http://10.10.10.126:8000", out _, out _));
            Assert.False(BambuddyConfigUri.TryParse("bb_abc123", out _, out _));
        }
    }
}
