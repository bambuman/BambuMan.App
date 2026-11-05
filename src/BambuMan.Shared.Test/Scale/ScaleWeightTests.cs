using BambuMan.Shared.Enums;
using BambuMan.Shared.Scale;

namespace BambuMan.Shared.Test.Scale
{
    [Trait("Category", "Scale")]
    public class ScaleWeightTests
    {
        private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

        private static ScaleReading Reading(decimal grams, bool stable, double secondsAgo = 0) =>
            new(grams, stable, ScaleProtocol.BambuMan, Now.AddSeconds(-secondsAgo));

        #region Conversion

        [Theory(DisplayName = "Kilograms and pounds convert to grams")]
        [InlineData(1.25, WeightUnit.Kilogram, 1250)]
        [InlineData(0.005, WeightUnit.Kilogram, 5)]
        [InlineData(1, WeightUnit.Pound, 453.59237)]
        [InlineData(2.2, WeightUnit.Pound, 997.903214)]
        public void ToGrams(decimal value, WeightUnit unit, decimal expected)
        {
            Assert.Equal(expected, ScaleWeight.ToGrams(value, unit));
        }

        [Theory(DisplayName = "The edit card gets the weight to 0.1 g")]
        [InlineData(997.903214, 997.9)]
        [InlineData(1234.55, 1234.6)]
        [InlineData(1250, 1250)]
        public void Round(decimal grams, decimal expected)
        {
            Assert.Equal(expected, ScaleWeight.Round(grams));
        }

        [Theory(DisplayName = "Filament left is the total minus the empty spool, never below zero")]
        [InlineData(1000, 250, 750)]
        [InlineData(200, 250, 0)]
        [InlineData(1000, null, 1000)]
        public void FilamentLeft(decimal gross, int? empty, decimal expected)
        {
            Assert.Equal(expected, ScaleWeight.FilamentLeft(gross, empty));
        }

        #endregion

        #region Weight at a tag read

        [Fact(DisplayName = "Nothing from the scale leaves the backend's weight")]
        public void ForSpoolRead_NoReading()
        {
            Assert.Null(ScaleWeight.ForSpoolRead(null, Now));
        }

        [Fact(DisplayName = "A fresh stable reading is used")]
        public void ForSpoolRead_FreshStable()
        {
            var reading = Reading(1180, true, 30);

            Assert.Same(reading, ScaleWeight.ForSpoolRead(reading, Now));
        }

        [Fact(DisplayName = "A fresh live reading is used while the scale settles")]
        public void ForSpoolRead_FreshLive()
        {
            var reading = Reading(1175, false, 1);

            Assert.Same(reading, ScaleWeight.ForSpoolRead(reading, Now));
        }

        [Theory(DisplayName = "Old readings are not used")]
        [InlineData(true, 61)]
        [InlineData(false, 4)]
        public void ForSpoolRead_Stale(bool stable, double secondsAgo)
        {
            Assert.Null(ScaleWeight.ForSpoolRead(Reading(1180, stable, secondsAgo), Now));
        }

        [Theory(DisplayName = "An empty scale is not a spool weight")]
        [InlineData(0)]
        [InlineData(-3.5)]
        [InlineData(19.9)]
        public void ForSpoolRead_EmptyScale(decimal grams)
        {
            Assert.Null(ScaleWeight.ForSpoolRead(Reading(grams, true), Now));
        }

        [Fact(DisplayName = "A reading that weighed another spool is not used for the next one")]
        public void ForSpoolRead_WeighedAnotherSpool()
        {
            // A Weight Scale Service scale sends its measurement once: lifting the spool off sends nothing.
            var reading = new ScaleReading(1180, true, ScaleProtocol.WeightScaleService, Now.AddSeconds(-20));

            Assert.Null(ScaleWeight.ForSpoolRead(reading, Now, reading));
            Assert.Null(ScaleWeight.ForSpoolRead(reading, Now, reading with { }));
        }

        [Fact(DisplayName = "A newer reading than the one that weighed another spool is used")]
        public void ForSpoolRead_NewerThanWeighedAnotherSpool()
        {
            var previous = Reading(1180, true, 20);
            var reading = Reading(950, true, 2);

            Assert.Same(reading, ScaleWeight.ForSpoolRead(reading, Now, previous));
        }

        #endregion

        #region Weight while the card is open

        [Fact(DisplayName = "Any spool reading fills a weight that didn't come from the scale yet")]
        public void ShouldReplace_NothingApplied()
        {
            Assert.True(ScaleWeight.ShouldReplace(null, Reading(900, false)));
            Assert.True(ScaleWeight.ShouldReplace(null, Reading(900, true)));
        }

        [Fact(DisplayName = "Live readings follow each other until the scale settles")]
        public void ShouldReplace_LiveOverLive()
        {
            Assert.True(ScaleWeight.ShouldReplace(Reading(800, false), Reading(900, false)));
        }

        [Fact(DisplayName = "A stable weight is not moved by live readings, only by the next stable one")]
        public void ShouldReplace_StablePreferred()
        {
            var stable = Reading(1000, true);

            Assert.False(ScaleWeight.ShouldReplace(stable, Reading(1100, false)));
            Assert.True(ScaleWeight.ShouldReplace(stable, Reading(1100, true)));
        }

        [Fact(DisplayName = "Lifting the spool off doesn't empty the weight")]
        public void ShouldReplace_EmptyScaleIgnored()
        {
            Assert.False(ScaleWeight.ShouldReplace(Reading(1000, false), Reading(0, true)));
        }

        #endregion
    }
}
