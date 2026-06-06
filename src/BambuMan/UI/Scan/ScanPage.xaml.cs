using System.Text.RegularExpressions;
using BambuMan.Shared;
using BarcodeScanning;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Scan
{
    public partial class ScanPage : IQueryAttributable
    {
        private readonly ILogger<ScanPage> logger;
        private bool isPageVisible;
        private string scanTarget = "spoolman_url";
        private bool requireUrl = true;

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("target", out var t) && t is string target)
            {
                scanTarget = target;
                // URLs are validated against the URL regex; API keys (and other free text) are not.
                requireUrl = target.EndsWith("url", StringComparison.OrdinalIgnoreCase);
            }
        }

        public ScanPage(ILogger<ScanPage> logger)
        {
            this.logger = logger;
            InitializeComponent();
            BackButton.Text = "<";
        }

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
                    await Shell.Current.GoToAsync("..");
                    return;
                }

                if (!MediaPicker.Default.IsCaptureSupported)
                {
                    logger.LogWarning("Camera capture not supported on this device, navigating back");
                    await Shell.Current.GoToAsync("..");
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

                var value = requireUrl
                    ? e.BarcodeResults.FirstOrDefault(x => Regex.IsMatch(x.RawValue, Constants.UrlValidation))?.RawValue ?? string.Empty
                    : e.BarcodeResults.FirstOrDefault()?.RawValue ?? string.Empty;
                if (string.IsNullOrEmpty(value)) return;

                Barcode.PauseScanning = true;
                await Shell.Current.GoToAsync("..", new Dictionary<string, object> { { "scan_target", scanTarget }, { "scan_value", value } });
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
                await Shell.Current.GoToAsync($"..");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in BackButton_Clicked ");
            }
        }
    }
}