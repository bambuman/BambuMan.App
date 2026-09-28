using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Resolvers;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.LocalInventory;

/// <summary>The spools the no-backend mode keeps on the device ("Store spools locally").</summary>
public partial class LocalInventoryPage
{
    private readonly LocalInventoryPageViewModel viewModel;
    private readonly ILogger<LocalInventoryPage> logger;
    private readonly NoBackendManager manager;

    public LocalInventoryPage(LocalInventoryPageViewModel viewModel, ILogger<LocalInventoryPage> logger, IInventoryBackendResolver backends)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        this.logger = logger;
        manager = (NoBackendManager)backends.Resolve(InventoryBackend.NoBackend);
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.Load(manager.Spools);
    }

    private async void Delete_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            if (sender is not Button { CommandParameter: string key }) return;

            await manager.RemoveSpoolAsync(key);
            viewModel.Load(manager.Spools);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Delete_OnClicked");
        }
    }

    private async void DeleteAll_OnClicked(object? sender, EventArgs e)
    {
        try
        {
            if (manager.Spools.Count == 0) return;

            if (!await DisplayAlertAsync("Delete all spools", $"Delete all {manager.Spools.Count} stored spools from this device? Export them to CSV first if you want to keep them.", "Delete all", "Cancel")) return;

            await manager.ClearSpoolsAsync();
            viewModel.Load(manager.Spools);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in DeleteAll_OnClicked");
        }
    }
}
