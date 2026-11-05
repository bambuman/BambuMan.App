using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.WriteTag;

/// <summary>Writes an OpenSpool tag from a filament entered by hand or copied from the last read spool.</summary>
public partial class WriteTagPage
{
    private readonly WriteTagModel model = new();

    [Inject] private ScannerService Scanner { get; set; } = null!;
    [Inject] private ILogger<WriteTagPage> Logger { get; set; } = null!;

    protected override void OnInitialized()
    {
        Scanner.TagWriteChanged += OnTagWriteChanged;
    }

    private void OnTagWriteChanged() => _ = InvokeAsync(StateHasChanged);

    private void CopyLastRead()
    {
        if (Scanner.LastReadTag != null) model.CopyFrom(Scanner.LastReadTag);
    }

    private void Write()
    {
        try
        {
            if (!model.Validate()) return;

            Scanner.StartOpenSpoolWrite(model.ToSpoolTag());
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error starting tag write");
        }
    }

    public void Dispose()
    {
        Scanner.TagWriteChanged -= OnTagWriteChanged;

        // Leaving the page gives the phone back to reading spools.
        if (Scanner.IsTagWritePending) Scanner.CancelTagWrite();
    }
}
