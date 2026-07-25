using BambuMan.Shared.Models;

namespace BambuMan.Shared
{
    /// <summary>
    /// The per-SKU matching exceptions compiled into every build. The BambuMan API serves its own copy of
    /// this set, so a new exception only needs an API redeploy to reach existing installs — and because the
    /// app compiles the same file, the next app release picks it up with no further work.
    /// <para>
    /// <b>Bump <see cref="CurrentVersion"/> whenever anything below changes</b>, otherwise clients that
    /// already cached an older set will not pick up the edit.
    /// </para>
    /// </summary>
    public static class FilamentMatchOverrides
    {
        public const int CurrentVersion = 2;

        public static FilamentOverrideSet Internal { get; } = new()
        {
            Version = CurrentVersion,

            #region Translucent catalog entries the catalog itself doesn't flag

            TransparentFilamentIds =
            [
                "bambulab_pc_clearblack_1000_175_n",
                "bambulab_pva_clear_500_175_n"
            ],

            #endregion

            #region Tag colour vs spoolman db colour mismatches

            ColorHexes =
            [
                new(new(DetailedFilamentType: "PLA Matte", Color: "E4BDD0"), "E8AFCF"), //ASA filament hex color is different on spoolman db vs tag
                new(new(FilamentType: "ASA", Color: "FFFFFF"), "FFFAF2"), //ASA filament hex color is different on spoolman db vs tag
                new(new(FilamentType: "ABS", Color: "ffb81c"), "FCE900"), //ABS filament hex color is different on spoolman db vs tag
                new(new(FilamentType: "ASA Aero", Color: "E9E4D9"), "F5F1DD"), //ASA filament hex color is different on spoolman db vs tag
                new(new(FilamentType: "PC", Color: "000000", Transparent: true), "5A5161"), //PC Clear Black filament hex color is different on spoolman db vs tag
                new(new(DetailedFilamentType: "PLA Wood", Color: "3F231C"), "4C241C"), //PETG HF red filament hex color is different on spoolman db vs tag
                new(new(DetailedFilamentType: "PETG HF", Color: "BC0900"), "EB3A3A"), //PETG HF red filament hex color is different on spoolman db vs tag
                new(new(DetailedFilamentType: "PETG Translucent", Color: "000000"), "FFFFFF") //PETG Translucent clear filament hex color is different on spoolman db vs tag
            ],

            #endregion

            #region Support materials

            SupportForcedIds =
            [
                //white translucent Support for PLA is identified as black. Don't know if black is same
                new(new(MaterialVariantIdentifier: "S05-C0", DetailedFilamentType: "Support for PLA"), "bambulab_pla_supportforpla/petgnature_500_175_n"),

                //white translucent Support for PLA is identified as black. Don't know if black is same
                new(new(MaterialVariantIdentifier: "S00-W0", DetailedFilamentType: "Support W"), "bambulab_pla_supportforplawhite_500_175_n"),
                new(new(MaterialVariantIdentifier: "S02-W1", DetailedFilamentType: "Support for PLA"), "bambulab_pla_supportforplawhite_500_175_n"),
                new(new(MaterialVariantIdentifier: "S02-W0", DetailedFilamentType: "Support for PLA"), "bambulab_pla_supportforplawhite_500_175_n"),

                //white translucent Support for PLA is identified as black. Don't know if black is same
                new(new(MaterialVariantIdentifier: "S03-G1", DetailedFilamentType: "Support For PA"), "bambulab_pa_supportforpa/pet_500_175_n")
            ],

            #endregion

            #region Multi colour spools reporting the wrong colours

            MultiColors =
            [
                new(new(MaterialVariantIdentifier: "A05-T1"), ["FF9425", "FCA2BF"]),
                new(new(MaterialVariantIdentifier: "A05-T2"), ["0047BB", "7D1B49"]),
                new(new(MaterialVariantIdentifier: "A05-T3"), ["0047BB", "BB22A3"]),
                new(new(MaterialVariantIdentifier: "A05-T4"), ["60A4E8", "4CE4A0"]),
                new(new(MaterialVariantIdentifier: "A05-T5"), ["000000", "A34342"]),
                new(new(MaterialVariantIdentifier: "A00-M5"), ["6FCAEF", "8573DD"]),
                new(new(MaterialVariantIdentifier: "A00-M6"), ["ED9558", "CE4406"])
            ],

            #endregion

            #region PC white / transparent (first match wins, so the second entry is the "not FC00" fallback)

            NameFilters =
            [
                new(new(UniqueMaterialIdentifier: "FC00", DetailedFilamentType: "PC", Color: "FFFFFF"), ["White", "Weiß"]),
                new(new(DetailedFilamentType: "PC", Color: "FFFFFF"), ["Transparent"])
            ],

            #endregion

            #region Forced ids (later entries win)

            ForcedIds =
            [
                new(new(DetailedFilamentType: "PC", Color: "68686580"), "bambulab_pc_clearblack_1000_175_n"),

                new(new(MaterialVariantIdentifier: "A00-W1"), "bambulab_pla_jadewhite_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A00-W01"), "bambulab_pla_jadewhite_1000_175_n"),
                new(new(MaterialVariantIdentifier: "S01-G1"), "bambulab_pa_supportforpa/pet_500_175_n"),
                new(new(MaterialVariantIdentifier: "S04-Y0"), "bambulab_pva_clear_500_175_n"),
                new(new(MaterialVariantIdentifier: "A00-Y00"), "bambulab_pla_yellow_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A00-B1"), "bambulab_pla_bluegray_1000_175_n"),
                new(new(MaterialVariantIdentifier: "G00-B00"), "bambulab_petg_basicreflexblue_1000_175_n"),
                new(new(MaterialVariantIdentifier: "G00-B0"), "bambulab_petg_blue_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A07-R5"), "bambulab_pla_redgranite_1000_175_n"),
                new(new(MaterialVariantIdentifier: "G01-N0"), "bambulab_petg_translucentbrown_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A00-P0"), "bambulab_pla_beige_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A01-B4"), "bambulab_pla_matteiceblue_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A00-P1"), "bambulab_pla_pink_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A19-P00"), "bambulab_pla_puremilkypink_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A19-B00"), "bambulab_pla_silk+babyblue_1000_175_n"),
                new(new(MaterialVariantIdentifier: "A19-A00"), "bambulab_pla_pureapricot_1000_175_n"),

                new(new(DetailedFilamentType: "PLA Basic", Color: "84754E"), "bambulab_pla_bronze_1000_175_n")
            ],

            #endregion

            #region Bambu filament codes, imported from the Bambu-Lab-RFID-Library README

            // Generated by BambuMan.Export — see FilamentCodeMap.g.cs. Regenerating it needs a CurrentVersion bump.
            FilamentCodes = FilamentCodeMap.ByMaterialVariant

            #endregion
        };
    }
}
