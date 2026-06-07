namespace BambuMan.Shared
{
    /// <summary>
    /// The combined Bambuddy setup QR payload: <c>bambuddy://config?v=1&amp;url=&lt;enc&gt;&amp;key=&lt;enc&gt;</c>.
    /// A single scan configures both the server URL and API key. This is a custom scheme (there is no standard for
    /// "server URL + API key" QR codes); the same format is documented for the Bambuddy side that generates it.
    /// </summary>
    public static class BambuddyConfigUri
    {
        public const string Scheme = "bambuddy";

        /// <summary>True when <paramref name="value"/> looks like a Bambuddy config URI (<c>bambuddy://…</c>).</summary>
        public static bool IsConfigUri(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.StartsWith($"{Scheme}://", StringComparison.OrdinalIgnoreCase);

        /// <summary>Build a config URI from a base URL + API key (used for round-trip tests / if we ever emit one).</summary>
        public static string Build(string url, string key) =>
            $"{Scheme}://config?v=1&url={Uri.EscapeDataString(url)}&key={Uri.EscapeDataString(key)}";

        /// <summary>
        /// Parse a config URI into its <paramref name="url"/> + <paramref name="key"/> (URL-decoded). Returns false
        /// when the value isn't a config URI or carries neither field. Either field may be null individually.
        /// </summary>
        public static bool TryParse(string? value, out string? url, out string? key)
        {
            url = null;
            key = null;

            if (!IsConfigUri(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;

            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;

                var name = pair[..eq];
                var val = Uri.UnescapeDataString(pair[(eq + 1)..]).Trim();

                if (name.Equals("url", StringComparison.OrdinalIgnoreCase)) url = val;
                else if (name.Equals("key", StringComparison.OrdinalIgnoreCase)) key = val;
            }

            url = string.IsNullOrEmpty(url) ? null : url;
            key = string.IsNullOrEmpty(key) ? null : key;

            return url != null || key != null;
        }
    }
}
