using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BambuMan.UI.Main
{
    /// <summary>
    /// Adds a spool whose tag can't be read (foil bag, damaged tag, a tag the phone can't unlock): the filament is picked
    /// from the catalog, or entered as unknown, and saved to the active backend with the usual edit card fields.
    /// </summary>
    public partial class AddSpoolPage
    {
        private const int DefaultLabelWeight = 1000;

        private readonly string materialListId = $"materials-{Guid.NewGuid():N}";

        private bool canAdd;

        [Inject] private AddSpoolPageViewModel Vm { get; set; } = null!;
        [Inject] private ScannerService Scanner { get; set; } = null!;
        [Inject] private ToastService Toasts { get; set; } = null!;
        [Inject] private NavigationManager Navigation { get; set; } = null!;
        [Inject] private ILogger<AddSpoolPage> Logger { get; set; } = null!;

        protected override void OnInitialized()
        {
            try
            {
                Observe(Vm);

                canAdd = Scanner.CanAddManualSpool;

                if (canAdd) Vm.Load(Scanner.ActiveManager.KnownFilaments);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error loading the filament catalog");
            }
        }

        private void Select(ManualFilamentItem item)
        {
            var filament = item.Filament;

            Vm.Select(item, Scanner.NewManualSpoolEdit((int)filament.Weight, filament.SpoolWeight));
        }

        private void SelectUnknown() => Vm.SelectUnknown(Scanner.NewManualSpoolEdit(Vm.UnknownLabelWeight ?? DefaultLabelWeight, null));

        private async Task SaveAsync()
        {
            var edit = Vm.Edit;

            if (Vm.Saving || edit == null) return;

            try
            {
                // Both checked, so every incomplete field is marked at once.
                var spool = Vm.BuildSpool();
                var valid = edit.Validate();

                if (spool == null || !valid) return;

                Vm.Saving = true;
                Vm.ErrorMessage = null;

                if (!await Scanner.AddManualSpoolAsync(spool, edit))
                {
                    Vm.ErrorMessage = "The spool couldn't be added. See logs.";
                    return;
                }

                Toasts.Show($"Spool {spool.Label} added", ToastKind.Success);
                GoBack();
            }
            catch (Exception e)
            {
                Vm.ErrorMessage = "The spool couldn't be added. See logs.";
                Logger.LogError(e, "Error adding a spool manually");
            }
            finally
            {
                Vm.Saving = false;
            }
        }

        private void GoBack() => Navigation.NavigateTo("/");
    }
}
