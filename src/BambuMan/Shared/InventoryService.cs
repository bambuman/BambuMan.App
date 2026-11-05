using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan
{
    public partial class InventoryService : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<InventoryModel> inventory = new();

        public bool HasItems => Inventory.Count > 0;

        public void InventorySpool(SpoolFound found, ISpoolTag tag)
        {
            if (MainThread.IsMainThread) InventorySpoolCore(found, tag);
            else MainThread.BeginInvokeOnMainThread(() => InventorySpoolCore(found, tag));
        }

        private void InventorySpoolCore(SpoolFound found, ISpoolTag tag)
        {
            // A spool added by hand has no tag to count it by, but it is always a new spool — give it a key of its own.
            var spoolId = tag.HasTag ? tag.SpoolId : Guid.NewGuid().ToString("N");

            if (string.IsNullOrEmpty(spoolId)) return;

            var inventoryModel = Inventory.FirstOrDefault(x => x.Material == found.Material);

            if (inventoryModel == null)
            {
                Inventory.Add(new InventoryModel(found.Material, spoolId));
                return;
            }

            if (!inventoryModel.Tags.Contains(spoolId))
            {
                inventoryModel.Quantity++;
                inventoryModel.Tags.Add(spoolId);

                // A count change isn't a collection change; tell the page so the chip re-renders.
                OnPropertyChanged(nameof(Inventory));
            }
        }

        public void Clear()
        {
            Inventory.Clear();
        }
    }
}
