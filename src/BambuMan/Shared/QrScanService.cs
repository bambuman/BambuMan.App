using BambuMan.UI.Scan;

namespace BambuMan
{
    /// <summary>Opens the native camera page over the Blazor host and returns what it read.</summary>
    public class QrScanService(IServiceProvider services)
    {
        /// <summary>The scanned value, or null when the user backed out or the camera was unavailable.</summary>
        public async Task<string?> ScanAsync(bool requireUrl)
        {
            var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
            if (navigation == null) return null;

            var page = services.GetRequiredService<ScanPage>();
            page.RequireUrl = requireUrl;

            await navigation.PushModalAsync(page);

            return await page.Result;
        }
    }
}
