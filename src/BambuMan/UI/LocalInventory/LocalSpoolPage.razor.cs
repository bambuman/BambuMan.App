using BambuMan.Components.Common;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using BambuMan.Shared.Resolvers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.LocalInventory;

/// <summary>One spool kept on the device: the same info a scan shows, and its edit card.</summary>
public partial class LocalSpoolPage
{
    private bool loading = true;
    private bool canEdit;
    private LocalSpool? spool;
    private SpoolDisplayInfo? info;
    private SpoolEditModel? edit;

    /// <summary>The spool's <see cref="LocalSpool.Key"/>: its tray uid, or tag uid without one.</summary>
    [Parameter] public string Key { get; set; } = string.Empty;

    [Inject] private IInventoryBackendResolver Backends { get; set; } = null!;
    [Inject] private ScannerService Scanner { get; set; } = null!;
    [Inject] private ToastService Toasts { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private ILogger<LocalSpoolPage> Logger { get; set; } = null!;

    private NoBackendManager Manager => (NoBackendManager)Backends.Resolve(InventoryBackend.NoBackend);

    private string Title => spool == null
        ? "Spool"
        : string.Join(" ", new[] { spool.Info.DetailedFilamentType ?? spool.Info.FilamentType, spool.Name }.Where(x => !string.IsNullOrEmpty(x)));

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            loading = true;

            await Scanner.PrepareLocalInventoryAsync();

            var manager = Manager;

            spool = manager.FindSpool(Key);
            canEdit = manager.CanEditSpools;
            info = spool == null ? null : await manager.DescribeAsync(spool);
            edit = spool == null ? null : SpoolEditModel.From(spool);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error loading spool {Key}", Key);
        }
        finally
        {
            loading = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            if (edit == null || !edit.Validate()) return;

            if (!await Manager.UpdateSpoolAsync(Key, edit.ToInput()))
            {
                Toasts.Show("This spool is no longer stored.", ToastKind.Error);
                return;
            }

            Toasts.Show("Spool saved", ToastKind.Success);
            GoBack();
        }
        catch (Exception e)
        {
            Toasts.Show("Saving the spool failed. See logs.", ToastKind.Error, true);
            Logger.LogError(e, "Error saving spool {Key}", Key);
        }
    }

    private async Task DeleteAsync()
    {
        try
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page == null) return;

            if (!await page.DisplayAlertAsync("Delete spool", $"Delete {Title} from this device?", "Delete", "Cancel")) return;

            await Manager.RemoveSpoolAsync(Key);

            Toasts.Show("Spool deleted");
            GoBack();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error deleting spool {Key}", Key);
        }
    }

    private void GoBack() => Navigation.NavigateTo("spools");
}
