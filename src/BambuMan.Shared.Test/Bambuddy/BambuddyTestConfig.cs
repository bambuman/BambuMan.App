namespace BambuMan.Shared.Test.Bambuddy
{
    /// <summary>
    /// Resolves Bambuddy integration-test credentials: environment variables first
    /// (<c>BAMBUDDY_TEST_URL</c> / <c>BAMBUDDY_TEST_KEY</c>), otherwise the gitignored
    /// <c>tmp/test_bambuddy.txt</c> walked up from the test output directory.
    /// </summary>
    internal static class BambuddyTestConfig
    {
        /// <param name="fileName">Credential file under <c>tmp/</c> (default = the current/prod test env).</param>
        /// <param name="envPrefix">Env-var prefix; resolves <c>{envPrefix}_URL</c> / <c>{envPrefix}_KEY</c> first.</param>
        public static bool TryLoad(out string url, out string key, string fileName = "test_bambuddy.txt", string envPrefix = "BAMBUDDY_TEST")
        {
            url = Environment.GetEnvironmentVariable($"{envPrefix}_URL") ?? string.Empty;
            key = Environment.GetEnvironmentVariable($"{envPrefix}_KEY") ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(key)) return true;

            var file = FindCredentialFile(fileName);
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

        private static string? FindCredentialFile(string fileName)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "tmp", fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
