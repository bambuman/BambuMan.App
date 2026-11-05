using BambuMan.Components.Common;
using BambuMan.Resources.Strings;
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
        ? AppResources.Main_SpoolSection
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
                Toasts.Show(AppResources.Spool_NoLongerStored, ToastKind.Error);
                return;
            }

            Toasts.Show(AppResources.Spool_Saved, ToastKind.Success);
            GoBack();
        }
        catch (Exception e)
        {
            Toasts.Show(AppResources.Spool_SaveFailed, ToastKind.Error, true);
            Logger.LogError(e, "Error saving spool {Key}", Key);
        }
    }

    /// <summary>Sends this spool to the selected server backend; the result is shown as a toast.</summary>
    private async Task SendAsync()
    {
        try
        {
            await Scanner.SendStoredSpoolsAsync([Key]);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error sending spool {Key}", Key);
        }
    }

    private async Task DeleteAsync()
    {
        try
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page == null) return;

            if (!await page.DisplayAlertAsync(AppResources.Spool_Delete, string.Format(AppResources.Spool_DeleteMessage, Title), AppResources.Common_Delete, AppResources.Common_Cancel)) return;

            await Manager.RemoveSpoolAsync(Key);

            Toasts.Show(AppResources.Spool_Deleted);
            GoBack();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error deleting spool {Key}", Key);
        }
    }

    private void GoBack() => Navigation.NavigateTo("spools");
}
