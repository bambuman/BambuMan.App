namespace BambuMan.Shared.Test
{
    public class ExtensionsTests
    {
        [Theory(DisplayName = "Server urls must be absolute http(s) urls")]
        [InlineData("http://192.168.1.10:8000", true)]
        [InlineData("https://spoolman.example.com/", true)]
        [InlineData("http://homeassistant.local:7912", true)]
        [InlineData("https://user:pass@nas.example.com:7912", true)]
        [InlineData(" http://spoolman:7912 ", true)]
        // "host:port" parses as a uri with the host as its scheme, which HttpClient rejects with NotSupportedException
        [InlineData("nas.example.com:7912", false)]
        [InlineData("nas.example.com", false)]
        [InlineData("ftp://nas.example.com", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsHttpUrl(string? url, bool expected)
        {
            Assert.Equal(expected, url.IsHttpUrl());
        }
    }
}
