using System.Text.RegularExpressions;
using BambuMan.Shared;
using BarcodeScanning;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Scan
{
    public partial class ScanPage
    {
        private readonly ILogger<ScanPage> logger;
        private bool isPageVisible;

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

                var url = e.BarcodeResults.FirstOrDefault(x => Regex.IsMatch(x.RawValue, Constants.UrlValidation))?.RawValue ?? string.Empty;
                if (string.IsNullOrEmpty(url)) return;

                Barcode.PauseScanning = true;
                await Shell.Current.GoToAsync("..", new Dictionary<string, object> { { "url", url } });
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