using BambuMan.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.PendingScans;

/// <summary>Scans the inventory server couldn't be reached for, waiting on the device to be sent.</summary>
public partial class PendingScansPage
{
    [Inject] private PendingScansPageViewModel Vm { get; set; } = null!;
    [Inject] private ILogger<PendingScansPage> Logger { get; set; } = null!;
    [Inject] private IPendingScanQueue Queue { get; set; } = null!;
    [Inject] private ScannerService Scanner { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Observe(Vm);
            Queue.OnChanged += OnQueueChanged;

            await Scanner.LoadPendingScansAsync();

            Load();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error loading queued scans");
        }
    }

    // A scan is queued, sent or rejected in the background; reload the list from the queue.
    private void OnQueueChanged() => _ = InvokeAsync(Load);

    private void Load() => Vm.Load(Queue.Items, Scanner.ActiveManager.Backend);

    private async Task SendAsync(Guid id)
    {
        try
        {
            await Scanner.SendPendingScanAsync(id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in SendAsync");
        }
    }

    private async Task SendAllAsync()
    {
        try
        {
            await Scanner.SendPendingScansAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in SendAllAsync");
        }
    }

    private async Task DiscardAsync(Guid id)
    {
        try
        {
            await Queue.RemoveAsync(id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in DiscardAsync");
        }
    }

    private async Task DiscardAllAsync()
    {
        try
        {
            if (Queue.Items.Count == 0) return;

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page == null) return;

            if (!await page.DisplayAlertAsync("Discard queued scans", $"Discard all {Queue.Items.Count} queued scans? They won't be sent to the inventory server.", "Discard all", "Cancel")) return;

            await Queue.ClearAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in DiscardAllAsync");
        }
    }

    public override void Dispose()
    {
        Queue.OnChanged -= OnQueueChanged;
        base.Dispose();
    }
}
