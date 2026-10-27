using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Resolvers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.LocalInventory;

/// <summary>The spools the no-backend mode keeps on the device ("Store spools locally").</summary>
public partial class LocalInventoryPage
{
    private NoBackendManager manager = null!;

    /// <summary>Groups the user has opened. Kept by group key, so they stay open across a filter change or reload.</summary>
    private readonly HashSet<string> expanded = [];

    [Inject] private LocalInventoryPageViewModel Vm { get; set; } = null!;
    [Inject] private ILogger<LocalInventoryPage> Logger { get; set; } = null!;
    [Inject] private IInventoryBackendResolver Backends { get; set; } = null!;
    [Inject] private ScannerService Scanner { get; set; } = null!;

    protected override void OnInitialized()
    {
        manager = (NoBackendManager)Backends.Resolve(InventoryBackend.NoBackend);

        Observe(Vm);
        manager.OnSpoolsChanged += OnSpoolsChanged;

        Vm.Load(manager.Spools);
    }

    // An import changes the store; reload the list from it.
    private void OnSpoolsChanged() => _ = InvokeAsync(() => Vm.Load(manager.Spools));

    private bool IsExpanded(LocalSpoolGroup group) => expanded.Contains(group.Key);

    private void Toggle(LocalSpoolGroup group)
    {
        if (!expanded.Remove(group.Key)) expanded.Add(group.Key);
    }

    private async Task DeleteAsync(string key)
    {
        try
        {
            await manager.RemoveSpoolAsync(key);
            Vm.Load(manager.Spools);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in DeleteAsync");
        }
    }

    private async Task DeleteAllAsync()
    {
        try
        {
            if (manager.Spools.Count == 0) return;

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page == null) return;

            if (!await page.DisplayAlertAsync("Delete all spools", $"Delete all {manager.Spools.Count} stored spools from this device? Export them to CSV first if you want to keep them.", "Delete all", "Cancel")) return;

            await manager.ClearSpoolsAsync();
            Vm.Load(manager.Spools);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in DeleteAllAsync");
        }
    }

    public override void Dispose()
    {
        manager.OnSpoolsChanged -= OnSpoolsChanged;
        base.Dispose();
    }
}
