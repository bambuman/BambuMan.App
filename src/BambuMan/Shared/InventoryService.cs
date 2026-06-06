using BambuMan.Shared;
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
            if (string.IsNullOrEmpty(info.TrayUid)) return;

            if (MainThread.IsMainThread) InventorySpoolCore(found, info);
            else MainThread.BeginInvokeOnMainThread(() => InventorySpoolCore(found, info));
        }

        private void InventorySpoolCore(SpoolFound found, BambuFilamentInfo info)
        {
            if (string.IsNullOrEmpty(info.TrayUid)) return;

            var inventoryModel = Inventory.FirstOrDefault(x => x.Material == found.Material);

            if (inventoryModel == null)
            {
                Inventory.Add(new InventoryModel(found.Material, info.TrayUid));
                return;
            }

            if (!inventoryModel.Tags.Contains(info.TrayUid))
            {
                inventoryModel.Quantity++;
                inventoryModel.Tags.Add(info.TrayUid);
            }
        }

        public void Clear()
        {
            Inventory.Clear();
        }
    }
}
