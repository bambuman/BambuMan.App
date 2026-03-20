using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Consent;

public partial class TagUploadConsentPopup
{
    private readonly ILogger<TagUploadConsentPopup> logger;

    public TagUploadConsentPopup(ILogger<TagUploadConsentPopup> logger)
    {
        InitializeComponent();
        this.logger = logger;
    }

    private async void OnAcceptClicked(object? sender, EventArgs e)
    {
        try
        {
            await CloseAsync(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in OnAcceptClicked");
        }
    }

    private async void OnDeclineClicked(object? sender, EventArgs e)
    {
        try
        {
            await CloseAsync(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in OnDeclineClicked");
        }
    }

    private async void OnLibraryLinkTapped(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.OpenAsync("https://github.com/queengooborg/Bambu-Lab-RFID-Library");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in OnLibraryLinkTapped");
        }
    }

    private async void OnBambuManLinkTapped(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.OpenAsync("https://bambuman.ee");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in OnBambuManLinkTapped");
        }
    }
}
