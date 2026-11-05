using BambuMan.Shared.Enums;

namespace BambuMan.Shared.Scale
{
    /// <summary>
    /// Turns scale readings into the spool edit card's weight. That field is the spool's current total weight — filament
    /// plus the empty spool — for every backend (each converts it to its own used weight with the empty weight), which
    /// is exactly what a scale shows with the spool put straight on it. So a reading goes in as it is, only in grams.
    /// </summary>
    public static class ScaleWeight
    {
        public const decimal GramsPerKilogram = 1000m;

        /// <summary>The international avoirdupois pound.</summary>
        public const decimal GramsPerPound = 453.59237m;

        /// <summary>Below this the scale is taken as empty (or the spool as lifted off), and the reading is not used.</summary>
        public const decimal MinimumSpoolGrams = 20m;

        /// <summary>How long a reading that is still settling is good for at a tag read.</summary>
        public static readonly TimeSpan LiveReadingMaxAge = TimeSpan.FromSeconds(3);

        /// <summary>
        /// How long a stable reading is good for at a tag read. Longer than a live one: a Weight Scale Service scale sends
        /// its final measurement once, and the tag is often read a little after the spool was put down.
        /// </summary>
        public static readonly TimeSpan StableReadingMaxAge = TimeSpan.FromSeconds(60);

        public static decimal ToGrams(decimal value, WeightUnit unit) => unit switch
        {
            WeightUnit.Pound => value * GramsPerPound,
            _ => value * GramsPerKilogram
        };

        /// <summary>The weight the edit card is given: to 0.1 g, the finest step any supported scale sends.</summary>
        public static decimal Round(decimal grams) => Math.Round(grams, 1, MidpointRounding.AwayFromZero);

        public static bool IsSpoolOnScale(ScaleReading reading) => reading.Grams >= MinimumSpoolGrams;

        /// <summary>The filament left on a spool weighing <paramref name="grossGrams"/> in all; never below zero.</summary>
        public static decimal FilamentLeft(decimal grossGrams, decimal? emptyWeight) => Math.Max(grossGrams - (emptyWeight ?? 0), 0);

        /// <summary>
        /// The reading to fill the weight with when a spool's tag is read: the latest one, if a spool is on the scale and
        /// the reading is still fresh. Null leaves the backend's weight in place.
        /// <paramref name="weighedAnotherSpool"/> is the reading already put in a different spool's card: a scale that sends
        /// a weight only once (Weight Scale Service) still has it as its latest after that spool was lifted off, and it is
        /// that spool's weight, not this one's.
        /// </summary>
        public static ScaleReading? ForSpoolRead(ScaleReading? latest, DateTime now, ScaleReading? weighedAnotherSpool = null)
        {
            if (latest == null || !IsSpoolOnScale(latest) || latest == weighedAnotherSpool) return null;

            var maxAge = latest.IsStable ? StableReadingMaxAge : LiveReadingMaxAge;

            return now - latest.ReceivedAt <= maxAge ? latest : null;
        }

        /// <summary>
        /// Whether <paramref name="incoming"/>, received while the edit card is open, should replace the weight filled from
        /// <paramref name="applied"/> (null when nothing came from the scale yet). A stable weight is preferred: once one
        /// is in, live readings no longer move it — only the next stable one does.
        /// </summary>
        public static bool ShouldReplace(ScaleReading? applied, ScaleReading incoming)
        {
            if (!IsSpoolOnScale(incoming)) return false;

            return incoming.IsStable || applied is not { IsStable: true };
        }
    }
}
