using BambuMan.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using SpoolMan.Api.Model;
using System.Collections.ObjectModel;

namespace BambuMan
{
    public partial class InventoryService : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<InventoryModel> inventory = new();

        public bool HasItems => Inventory.Count > 0;

        public void InventorySpool(Spool spool, BambuFillamentInfo info)
        {
            if (info.TrayUid == null) return;

            var inventoryModel = Inventory.FirstOrDefault(x => x.Material == spool.Filament.Material);

            if (inventoryModel == null)
            {
                Inventory.Add(new InventoryModel(spool.Filament.Material, info.TrayUid));
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
