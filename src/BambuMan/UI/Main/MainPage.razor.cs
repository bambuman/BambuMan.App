using BambuMan.Components.Common;
using BambuMan.Interfaces;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
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
            { SpoolmanConnecting: true } => $"Connecting to {Vm.BackendLabel} …",
            { ErrorMessage: not null } => "Needs attention",
            { NfcIsEnabled: false } => "NFC is disabled",
            _ => "Ready to read spools"
        };

        private string NfcStatus => Vm switch
        {
            { NfcIsEnabled: false } => "error",
            { DeviceIsListening: true } => "success",
            _ => "idle"
        };

        private (string Kind, string Text) BackendPill => Vm switch
        {
            { Backend: InventoryBackend.NoBackend } => ("primary", "On this device"),
            { SpoolmanConnecting: true } => ("info", "Connecting"),
            { SpoolmanOk: true } => ("success", "Connected"),
            { BackendStatus: ManagerStatusType.ApiUrlMissing } => ("error", "Url missing"),
            { BackendStatus: ManagerStatusType.CantConnectToApi } => ("error", "Unreachable"),
            _ => ("error", "Offline")
        };

        protected override async Task OnInitializedAsync()
        {
            try
            {
                Observe(Vm);
                ObserveCollection(Vm.Logs);
                Observe(InventoryService);
                ObserveCollection(InventoryService.Inventory);

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

                        await spoolEditForm.FocusWeightAsync();
                        SoftKeyboard.Show();
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

        public override void Dispose()
        {
            Scanner.SpoolEditFocusRequested -= OnSpoolEditFocusRequested;
            base.Dispose();
        }
    }
}
