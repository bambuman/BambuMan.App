using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BambuMan.Components.Common
{
    /// <summary>The fields of the spool edit card, shared by the scan page and the stored spool page.</summary>
    public partial class SpoolEditModel : ObservableObject
    {
        [ObservableProperty] private decimal? weight;
        [ObservableProperty] private decimal? emptyWeight = 250;
        [ObservableProperty] private decimal? price;
        [ObservableProperty] private DateTime? buyDate;
        [ObservableProperty] private string? lotNr;
        [ObservableProperty] private string? location;

        [ObservableProperty] private string? weightError;
        [ObservableProperty] private string? emptyWeightError;
        [ObservableProperty] private string? priceError;

        public static SpoolEditModel From(SpoolFound found) => new()
        {
            Weight = found.Weight,
            EmptyWeight = found.EmptyWeight,
            Price = found.Price,
            BuyDate = found.BuyDate,
            LotNr = found.LotNr,
            Location = found.Location
        };

        public static SpoolEditModel From(LocalSpool spool) => new()
        {
            Weight = spool.Weight,
            EmptyWeight = spool.EmptyWeight,
            Price = spool.Price,
            BuyDate = spool.BuyDate,
            LotNr = spool.LotNr,
            Location = spool.Location
        };

        /// <summary>Checks the card before it is written back; each failing field gets its message.</summary>
        public bool Validate()
        {
            WeightError = Weight < 0 ? "Weight can't be negative." : null;
            EmptyWeightError = EmptyWeight < 0 ? "Empty weight can't be negative." : null;
            PriceError = Price < 0 ? "Price can't be negative." : null;

            return WeightError == null && EmptyWeightError == null && PriceError == null;
        }

        public SpoolEditInput ToInput() => new(Weight, EmptyWeight, Price, BuyDate, LotNr, Location);
    }
}
