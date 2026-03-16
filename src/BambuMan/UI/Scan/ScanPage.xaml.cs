using System.Text.RegularExpressions;
using BambuMan.Shared;
using BarcodeScanning;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Scan
{
    public partial class ScanPage
    {
        private readonly ILogger<ScanPage> logger;

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
                await Methods.AskForRequiredPermissionAsync();
                base.OnAppearing();

                Barcode.CameraEnabled = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OnAppearing ");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Barcode.CameraEnabled = false;
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