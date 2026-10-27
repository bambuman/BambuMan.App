using System.Text.RegularExpressions;
using BambuMan.Shared;
using BarcodeScanning;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Scan
{
    /// <summary>
    /// Native camera page, pushed modally over the Blazor host by <see cref="QrScanService"/>. The scanned value (or
    /// null when the user backs out) is handed back through <see cref="Result"/>.
    /// </summary>
    public partial class ScanPage
    {
        private readonly ILogger<ScanPage> logger;
        private readonly TaskCompletionSource<string?> result = new();
        private bool isPageVisible;

        public ScanPage(ILogger<ScanPage> logger)
        {
            this.logger = logger;
            InitializeComponent();
            BackButton.Text = "<";
        }

        /// <summary>URLs are validated against the URL regex; API keys (and other free text) are not.</summary>
        public bool RequireUrl { get; set; } = true;

        public Task<string?> Result => result.Task;

        protected override async void OnAppearing()
        {
            try
            {
                isPageVisible = true;

                var granted = await Methods.AskForRequiredPermissionAsync();
                base.OnAppearing();

                if (!isPageVisible)
                {
                    logger.LogWarning("Page dismissed during permission request, skipping camera start");
                    return;
                }

                if (!granted)
                {
                    logger.LogWarning("Camera permission not granted, navigating back");
                    await CloseAsync(null);
                    return;
                }

                if (!MediaPicker.Default.IsCaptureSupported)
                {
                    logger.LogWarning("Camera capture not supported on this device, navigating back");
                    await CloseAsync(null);
                    return;
                }

                // Small delay to let CameraX InitializationFuture complete before binding to lifecycle
                await Task.Delay(100);

                if (!isPageVisible)
                {
                    logger.LogWarning("Page dismissed during camera initialization, skipping camera start");
                    return;
                }

                Barcode.CameraEnabled = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OnAppearing ");
            }
        }

        protected override void OnDisappearing()
        {
            try
            {
                isPageVisible = false;
                base.OnDisappearing();
                Barcode.CameraEnabled = false;

                // Hardware back pops the modal without going through CloseAsync.
                result.TrySetResult(null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OnDisappearing");
            }
        }

        private async void CameraView_OnDetectionFinished(object sender, OnDetectionFinishedEventArg e)
        {
            try
            {
                if (!e.BarcodeResults.Any()) return;

                // A combined Bambuddy config QR (url + key) is accepted whichever field was being scanned.
                var combined = e.BarcodeResults.FirstOrDefault(x => BambuddyConfigUri.IsConfigUri(x.RawValue))?.RawValue;
                var value = combined ?? (RequireUrl
                    ? e.BarcodeResults.FirstOrDefault(x => Regex.IsMatch(x.RawValue, Constants.UrlValidation))?.RawValue ?? string.Empty
                    : e.BarcodeResults.FirstOrDefault()?.RawValue ?? string.Empty);
                if (string.IsNullOrEmpty(value)) return;

                Barcode.PauseScanning = true;
                await CloseAsync(value);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in CameraView_OnDetectionFinished ");
            }
        }

        private async void BackButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                await CloseAsync(null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in BackButton_Clicked ");
            }
        }

        private async Task CloseAsync(string? value)
        {
            if (!result.TrySetResult(value)) return;

            await Navigation.PopModalAsync();
        }
    }
}
