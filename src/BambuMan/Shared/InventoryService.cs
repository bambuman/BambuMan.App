using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan
{
    public partial class InventoryService : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<InventoryModel> inventory = new();

        public bool HasItems => Inventory.Count > 0;

        public void InventorySpool(SpoolFound found, BambuFilamentInfo info)
        {
            if (MainThread.IsMainThread) InventorySpoolCore(found, info);
            else MainThread.BeginInvokeOnMainThread(() => InventorySpoolCore(found, info));
        }

        private void InventorySpoolCore(SpoolFound found, BambuFilamentInfo info)
        {
            // A spool added by hand has no tag to count it by, but it is always a new spool — give it a key of its own.
            var tag = info.HasTag ? info.TrayUid : Guid.NewGuid().ToString("N");

            if (string.IsNullOrEmpty(tag)) return;

            var inventoryModel = Inventory.FirstOrDefault(x => x.Material == found.Material);

            if (inventoryModel == null)
            {
                Inventory.Add(new InventoryModel(found.Material, tag));
                return;
            }

            if (!inventoryModel.Tags.Contains(tag))
            {
                inventoryModel.Quantity++;
                inventoryModel.Tags.Add(tag);

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
