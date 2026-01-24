using CommunityToolkit.Maui.Views;

namespace BambuMan.UI.Consent;

public partial class TagUploadConsentPopup : Popup<bool>
{
    public TagUploadConsentPopup()
    {
        InitializeComponent();
    }

    private async void OnAcceptClicked(object? sender, EventArgs e) => await CloseAsync(true);

    private async void OnDeclineClicked(object? sender, EventArgs e) => await CloseAsync(false);

    private async void OnLibraryLinkTapped(object? sender, EventArgs e)
    {
        await Launcher.OpenAsync("https://github.com/queengooborg/Bambu-Lab-RFID-Library");
    }

    private async void OnBambuManLinkTapped(object? sender, EventArgs e)
    {
        await Launcher.OpenAsync("https://bambuman.ee");
    }
}
