namespace BambuMan.Shared.Test.Bambuddy
{
    /// <summary>
    /// Resolves Bambuddy integration-test credentials: environment variables first
    /// (<c>BAMBUDDY_TEST_URL</c> / <c>BAMBUDDY_TEST_KEY</c>), otherwise the gitignored
    /// <c>tmp/test_bambuddy.txt</c> walked up from the test output directory.
    /// </summary>
    internal static class BambuddyTestConfig
    {
        public static bool TryLoad(out string url, out string key)
        {
            url = Environment.GetEnvironmentVariable("BAMBUDDY_TEST_URL") ?? string.Empty;
            key = Environment.GetEnvironmentVariable("BAMBUDDY_TEST_KEY") ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key)) return true;

            var file = FindCredentialFile();
            if (file == null) return false;

            foreach (var line in File.ReadAllLines(file))
            {
                var idx = line.IndexOf(':');
                if (idx < 0) continue;

                var label = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();

                if (label.Equals("API Url", StringComparison.OrdinalIgnoreCase)) url = value;
                else if (label.Equals("API Key", StringComparison.OrdinalIgnoreCase)) key = value;
            }

            return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key);
        }

        private static string? FindCredentialFile()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "tmp", "test_bambuddy.txt");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
