using BambuMan.Components.Common;
using BambuMan.Interfaces;
using BambuMan.Resources.Strings;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using BambuMan.Shared.Scale;
using BambuMan.UI.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BambuMan.UI.Main
{
    public partial class MainPage
    {
        /// <summary>The first visit redirects to settings when the server url is missing; later visits don't nag.</summary>
        private static bool startupChecked;

        private SpoolEditForm? spoolEditForm;

        [Inject] private ScannerService Scanner { get; set; } = null!;
        [Inject] private InventoryService InventoryService { get; set; } = null!;
        [Inject] private IScaleService ScaleService { get; set; } = null!;
        [Inject] private ISoftKeyboard SoftKeyboard { get; set; } = null!;
        [Inject] private IJSRuntime Js { get; set; } = null!;
        [Inject] private NavigationManager Navigation { get; set; } = null!;
        [Inject] private ILogger<MainPage> Logger { get; set; } = null!;

        private MainPageViewModel Vm => Scanner.ViewModel;

        private int SessionSpoolCount => InventoryService.Inventory.Sum(x => x.Quantity);

        /// <summary>The swatch for the edit card, built from the tag itself — the editable backends don't hand over a catalog match.</summary>
        private SpoolDisplayInfo? SpoolColors => Vm.SpoolTag == null ? null : SpoolDisplayInfo.From(Vm.SpoolTag, null);

        private string StatusDot => Vm switch
        {
            { SpoolmanConnecting: true } => "info",
            { ErrorMessage: not null } => "error",
            { NfcIsEnabled: false } => "caution",
            _ => "success"
        };

        private string StatusText => Vm switch
        {
            { SpoolmanConnecting: true } => string.Format(AppResources.Main_StatusConnecting, Vm.BackendLabel),
            { ErrorMessage: not null } => AppResources.Main_StatusNeedsAttention,
            { NfcIsEnabled: false } => AppResources.Main_StatusNfcDisabled,
            _ => AppResources.Main_StatusReady
        };

        private string NfcStatus => Vm switch
        {
            { NfcIsEnabled: false } => "error",
            { DeviceIsListening: true } => "success",
            _ => "idle"
        };

        /// <summary>
        /// Under the weight field: that the weight came from the scale (with the filament that leaves on the spool), or
        /// what keeps the scale from filling it. Nothing without a scale, or once the user typed a weight of their own.
        /// </summary>
        private string? ScaleWeightHelp
        {
            get
            {
                var edit = Vm.SpoolEdit;

                if (ScaleService.DeviceId == null || edit.WeightTyped) return null;

                if (Vm.AppliedScaleReading is { } reading && edit.Weight is { } weight)
                    return string.Format(reading.IsStable ? AppResources.Scale_WeightFromScale : AppResources.Scale_WeightSettling, $"{ScaleWeight.FilamentLeft(weight, edit.EmptyWeight):0}");

                return ScaleService.State == ScaleConnectionState.Connected
                    ? AppResources.Scale_PutOnScale
                    : string.Format(AppResources.Scale_State, ScaleService.State.DisplayName());
            }
        }

        private (string Kind, string Text) BackendPill => Vm switch
        {
            { Backend: InventoryBackend.NoBackend } => ("primary", AppResources.Main_BackendOnDevice),
            { SpoolmanConnecting: true } => ("info", AppResources.Main_BackendConnecting),
            { SpoolmanOk: true } => ("success", AppResources.Main_BackendConnected),
            { BackendStatus: ManagerStatusType.ApiUrlMissing } => ("error", AppResources.Main_BackendUrlMissing),
            { BackendStatus: ManagerStatusType.CantConnectToApi } => ("error", AppResources.Main_BackendUnreachable),
            _ => ("error", AppResources.Main_BackendOffline)
        };

        protected override async Task OnInitializedAsync()
        {
            try
            {
                Observe(Vm);
                ObserveCollection(Vm.Logs);
                Observe(InventoryService);
                ObserveCollection(InventoryService.Inventory);
                Observe(ScaleService);

                Scanner.SpoolEditFocusRequested += OnSpoolEditFocusRequested;

                if (!startupChecked)
                {
                    startupChecked = true;

                    if (SettingsPage.IsServerUrlMissing())
                    {
                        Navigation.NavigateTo("settings");
                        return;
                    }
                }

                await Scanner.ActivateAsync();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error in OnInitializedAsync");
            }
        }

        private async void OnSpoolEditFocusRequested(bool focus)
        {
            try
            {
                await InvokeAsync(async () =>
                {
                    if (focus)
                    {
                        // Render first, so the card (and the weight field) exists before it is focused.
                        StateHasChanged();

                        if (spoolEditForm == null) return;

                        // Native focus first: the field can only take the keyboard once the web view holds focus.
                        SoftKeyboard.Show();
                        await spoolEditForm.FocusWeightAsync();
                    }
                    else
                    {
                        await Js.InvokeVoidAsync("bambuman.blur");
                        SoftKeyboard.Hide();
                    }
                });
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error in OnSpoolEditFocusRequested");
            }
        }

        private async Task SaveAsync()
        {
            await Scanner.SaveSpoolAsync();
        }

        /// <summary>A spool whose tag can't be read (foil bag, damaged tag) is added from its own page.</summary>
        private void AddSpoolManually() => Navigation.NavigateTo("add-spool");

        public override void Dispose()
        {
            Scanner.SpoolEditFocusRequested -= OnSpoolEditFocusRequested;
            base.Dispose();
        }
    }
}
