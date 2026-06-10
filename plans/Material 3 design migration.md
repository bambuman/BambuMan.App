# Migrate BambuMan from UraniumUI → Material 3 (HorusStudio + native M3)

## Context

The BambuMan MAUI app currently renders its UI with **UraniumUI Material 2.16.0** (TextField / AutoCompleteTextField / DatePickerField inputs, a `StyleResource` theme layer, and the FontAwesome + MaterialSymbols icon fonts). The look is dated and inconsistent: the light theme is brown (`#7E570E`) while the dark theme is an unrelated lavender (`#ac99ea`), `Border` defaults to sharp `Rectangle` corners, the `Shadow` is a white 10×10 drop shadow, buttons are below M3/WCAG touch-target size, and navigation uses a flyout drawer for only three destinations. `docs/design-review.md` audits every page against Material 3 and gives concrete fixes.

The goal: **remove UraniumUI entirely**, move to **Material 3** using the already-referenced `HorusStudio.Maui.MaterialDesignControls 10.0.1` for controls plus MAUI's native `<UseMaterial3>` flag, **consolidate all icons onto a single self-hosted Material Symbols font**, and **regenerate a coherent light+dark M3 palette** from the existing brown brand seed. Outcome: a consistent, modern, accessible Material 3 baseline with one icon font and no UraniumUI dependency.

## Decisions (confirmed with user)

- **Scope:** *Foundation + full control swap.* Regenerate palette/styles/typography, switch to bottom navigation, replace **all** UraniumUI controls with HorusStudio, consolidate icons, remove UraniumUI entirely, plus the cheap high-value page fixes. The larger per-page UX additions are **deferred** (listed at the end).
- **Control libraries:** *Native M3 + HorusStudio (hybrid).* Lean on MAUI's native `<UseMaterial3>` (10.0.60+; the app is on **10.0.70**) for `Button`, `Entry`/`Editor`, **`Switch`**, **`CheckBox`**, `RadioButton`, `Slider`, `ProgressBar`, `Picker`, `Date`/`TimePicker`, `ImageButton`, and the **Shell bottom tab bar** (native M3 `BottomNavigationView`). Use **HorusStudio** only for the expressive composites native lacks (`MaterialTextField`, `MaterialChip`, `MaterialCard`, `MaterialFloatingButton`, `MaterialSegmentedButton`). Use **CommunityToolkit.Maui `Snackbar`** (already referenced) for save confirmations. No heavyweight suite (Syncfusion/DevExpress/Telerik) — keeps the app lean, free, and purely M3. No single pack is a strictly-better *pure-M3* superset of HorusStudio; the only gap is autocomplete.
- **Location fields:** Build a **custom autocomplete** control (HorusStudio has none; `MaterialSearch` is "coming soon"; native MAUI has no autocomplete). Must preserve type-ahead suggestions **and** entering brand-new location names.
- **Icons:** **Self-host Google's Material Symbols `.ttf`** (Apache-2.0) + a generated glyph-constants class. Keeps the existing `FontImageSource` + `{x:Static m:MaterialSharp.X}` pattern, so per-site churn is just an `xmlns` swap. (Compose Material3 icons are Kotlin/Compose assets — not usable in MAUI XAML. MauiIcons uses its own markup extension, not `FontImageSource` — rejected: high churn, awkward `Shell.Icon` support.)

## Key facts established during investigation

- **HorusStudio coverage** maps cleanly: `MaterialTextField` (Text, Placeholder, Label, SupportingText, LeadingIcon/TrailingIcon as `ImageSource`, LeadingIconCommand/TrailingIconCommand, Keyboard, HasError/ErrorIcon), `MaterialButton` (`Type` = `MaterialButtonType.{Elevated,Filled,Tonal,Outlined,Text}`, Text, Command/Clicked, ImageSource, CornerRadius), `MaterialSwitch`, `MaterialCheckbox`, `MaterialDatePicker` (**nullable `Date` + `Placeholder`**), `MaterialChip`, `MaterialCard`, `MaterialFloatingButton`, `MaterialSnackbar` (`IMaterialSnackbar` DI service → `Show(new MaterialSnackbarConfig("…"))`), `MaterialSegmentedButton` (already used in Settings).
- **`MaterialDatePicker` nullable `Date`** makes the custom `UI/Controls/NullableDatePickerField.cs` (a UraniumUI workaround) **deletable**.
- **`<UseMaterial3>`** is Android-only and restyles native controls. Per the [.NET blog](https://devblogs.microsoft.com/dotnet/dotnet-maui-material-3/), coverage landed incrementally: SR4 (10.0.40) `CheckBox`; **SR6 (10.0.60)** `Button`, `Entry`, `SearchBar`, `DatePicker`, `Slider`, `ProgressBar`, `ImageButton`, **`Switch`**, and **`Shell`** theming. The app is on **10.0.70**, so the full native M3 set is available — including the **Shell bottom tab bar as M3 `BottomNavigationView`**. The blog clarifies that explicit styles/colors you set still take precedence (so our M3-aligned `Styles.xaml` values win where set) — no need to strip `Styles.xaml`. Native has **no** autocomplete/combobox and no Chip/Card/FAB/SegmentedButton — those stay on HorusStudio. The bottom-nav `TabBar` is already stubbed/commented in `AppShell.xaml:43`.
- The **theme-change shell-recreation** in `App.xaml.cs` exists *specifically* because UraniumUI caches colors at construction (issue #660). Removing UraniumUI should let us drop it; the **separate** Glide/`dotnet/maui#12513` window stop/resume icon-clear workaround stays (we still use `FontImageSource`). Verify live theme switching during implementation.

---

## Implementation

### Phase 0 — Project & DI plumbing

- `src/BambuMan/BambuMan.csproj`: add `<UseMaterial3>true</UseMaterial3>` to the main `<PropertyGroup>`. **Remove** `UraniumUI.Material`, `UraniumUI.Icons.FontAwesome`, `UraniumUI.Icons.MaterialSymbols` package references (do this last, in Phase 6, once nothing references them). Keep `HorusStudio.Maui.MaterialDesignControls`.
- `src/BambuMan/MauiProgram.cs`:
  - Remove `.UseUraniumUI()`, `.UseUraniumUIMaterial()`, `using UraniumUI;`.
  - In `ConfigureFonts`: remove `fonts.AddFontAwesomeIconFonts()` and `fonts.AddMaterialSymbolsFonts()`; add `fonts.AddFont("MaterialSymbols.ttf", "MaterialSharp")` (keep the alias `MaterialSharp` so every existing `FontFamily="MaterialSharp"` string is unchanged).
  - Extend the existing `.UseMaterialDesignControls(...)` `ConfigureThemes` from the segmented-button-only partial merge to the **full M3 token set**, sourced from the regenerated `Colors.xaml` (read via `(Color)Application.Current.Resources["Primary"]` etc., or `options.ConfigureThemesFromResources(...)`) so every library control inherits the brand — single source of truth in `Colors.xaml`.
- `src/BambuMan/App.xaml`: remove `xmlns:m="…UraniumUI.Material.Resources…"` and the `<m:StyleResource …/>` merged dictionary line. Keep `Colors.xaml`, `Styles.xaml`, and `Override.xaml` (Override.xaml is gutted in Phase 6).

### Phase 1 — Foundation: palette, styles, typography (design review §2, §8.1)

- **`Resources/Styles/Colors.xaml` — regenerate wholesale** from a single seed `#7E570E` into a proper M3 tonal palette, defining light + dark pairs for the standard role names already in use (`Primary`/`PrimaryDark`, `OnPrimary`, `PrimaryContainer`, `Secondary`, `Tertiary`, `Surface` + `SurfaceContainer` tiers, `SurfaceVariant`, `Outline`/`OutlineVariant`, `Error`/`ErrorContainer`, `Background`, etc.). Use [m3.material.io/theme-builder](https://m3.material.io/theme-builder/) output as the source of token values. Then:
  - Derive the six banner blocks (Error/Success/Info/Version/Warning/Caution) from M3 roles instead of bespoke hand-mixed colors (error = `ErrorContainer`/`Error`/`OnErrorContainer`; success = a tertiary green; info = secondary/primary container).
  - Add `SurfaceContainer`/`SurfaceContainerDark` and replace the one-off `#F7F4F6`/`#2C2A30` (MainPage toggle card, §3.4) and other inline hex (§8.2) with tokens.
  - Delete the unused accent blocks (`Yellow*Accent`, `Cyan*Accent`, `Blue*Accent`, `Crimson`, `DodgerBlue`) and the `SecondaryButton*`/`TertiaryButton*` blocks (superseded by M3 button variants).
  - Update `Platforms/Android/Resources/values/colors.xml` (`colorPrimary`/`colorPrimaryDark`/`colorAccent`) to match the new seed-derived tokens; optionally add `values-night/colors.xml`.
- **`Resources/Styles/Styles.xaml`:**
  - `Border` default: remove `StrokeShape="Rectangle"` (or set `RoundRectangle 12`) so M3 rounded corners aren't globally overridden (§2.2).
  - `Shadow` default: `Radius=12`, `Opacity=0.15`, `Brush=Black`, `Offset=0,4` (§2.3).
  - `Button` default: `Padding="24,10"`, `MinimumHeightRequest="40"`, `CornerRadius="20"`, add `Pressed`/`Focused` visual states (§2.4). (Native Button only — most buttons become `MaterialButton` in Phase 4.)
  - Add the **M3 type scale** as named `Label` styles: `DisplayMedium`, `HeadlineMedium`, `TitleLarge`, `BodyLarge`, `BodyMedium`, `LabelMedium` (§8.1). Replace ad-hoc inline `FontSize` values as pages are touched.

### Phase 2 — Icons: self-host Material Symbols, drop FontAwesome

- Download **Material Symbols (Sharp)** static `.ttf` from Google Fonts (Apache-2.0) → `src/BambuMan/Resources/Fonts/MaterialSymbols.ttf`; register with alias `MaterialSharp` (Phase 0).
- Generate `src/BambuMan/Resources/Icons/MaterialSharp.cs` — a static class named **`MaterialSharp`** (so `{x:Static m:MaterialSharp.House}` is byte-for-byte unchanged) with `public const string` members for the glyphs in use, built from the font's `.codepoints` file. Use the local Python (`C:\Users\arvi.saluste\AppData\Local\Programs\Python\Python314\python.exe`) to emit the class. Include the 17 current Material Symbols glyphs **plus** the new ones the cheap fixes need (e.g. `Check_circle`, `Error`, `Circle`/`Brightness_1`).
- In each XAML file, change only the icon `xmlns` from `clr-namespace:UraniumUI.Icons.MaterialSymbols;assembly=UraniumUI.Icons.MaterialSymbols` to `clr-namespace:BambuMan.Resources.Icons` (keep alias `m`). Files: `AppShell.xaml`, `UI/Main/MainPage.xaml`, `UI/Settings/SettingsPage.xaml`, `UI/Consent/TagUploadConsentPopup.xaml`.
- Replace the **3 FontAwesome** usages with Material Symbols and remove every `xmlns:fa` (`UI/Main/MainPage.xaml`, `UI/Settings/SettingsPage.xaml`, `UI/Logs/LogsPage.xaml`): `Solid.Gear`→`MaterialSharp.Settings`, `Solid.House`→`MaterialSharp.House`, `Solid.Circle`→`MaterialSharp.Circle`.

### Phase 3 — Navigation: bottom nav (design review §2.5)

- `src/BambuMan/AppShell.xaml`: remove `FlyoutBehavior="Flyout"` and the `<Shell.ItemTemplate>`; uncomment/extend the `<TabBar>` block (lines 43–56) to include all three destinations (Main, Settings, Logs) with their Material Symbols icons. Keep the `StatusBarBehavior`. With `<UseMaterial3>` on 10.0.70 this renders as the native M3 `BottomNavigationView` — no third-party nav control needed.
- Remove the now-redundant toolbar items that duplicated flyout nav: the `Settings` toolbar item on `MainPage` and the `Home` toolbar items on `SettingsPage`/`LogsPage` (§2.5, §4.4). This also removes the last FontAwesome `Solid.House`/`Solid.Gear` toolbar usages.

### Phase 4 — Control swap (UraniumUI → HorusStudio)

Division of labor: **native M3** for simple inputs/toggles/buttons-of-last-resort, **HorusStudio** for the composites native can't express. Replace `xmlns:material="http://schemas.enisn-projects.io/…uraniumui/material"` with `xmlns:material="clr-namespace:HorusStudio.Maui.MaterialDesignControls;assembly=HorusStudio.Maui.MaterialDesignControls"` in `MainPage.xaml` and `SettingsPage.xaml`, then:

- **TextField → `MaterialTextField`** (9 sites: MainPage 4, Settings 5). Map `Title`→`Label`, keep `Icon`→`LeadingIcon` (now an `ImageSource`/`FontImageSource`), `Keyboard`, clear via `TrailingIcon` + `TrailingIconCommand`. Use `SupportingText`/`HasError` for validation messaging where the old fields showed it.
- **AutoCompleteTextField → custom `MaterialAutoCompleteField`** (2 sites: MainPage location, Settings location). See design below. Settings' "refresh locations" becomes the field's `TrailingIcon` + `TrailingIconCommand`.
- **CheckBox → native `Switch`** (M3 at 10.0.70) for the 5 on/off settings (§4.3 — switches, not checkboxes, are the M3 settings pattern), placed in an M3 list-item row (label + optional supporting text + trailing `Switch`). HorusStudio `MaterialSwitch` is an option if its built-in label/supporting text is more convenient. No multi-select sites need a checkbox.
- **`NullableDatePickerField` → HorusStudio `MaterialDatePicker`** (`Date` nullable + `Placeholder`); **delete** `UI/Controls/NullableDatePickerField.cs` and its `Override.xaml` style. (Native MAUI `DatePicker` can't be null, so HorusStudio's nullable date is the right fit here.)
- **Buttons → HorusStudio `MaterialButton`** with `Type` per hierarchy (§3.5, §4.2, §7.4): primary/positive = `Filled`; secondary = `Outlined` or `Text`; replace the hardcoded-Crimson "Scan QR"/"Refresh" and the Cancel/Save and consent No-thanks/Participate pairs. (Native M3 `Button` is single-style; HorusStudio's `Type` enum gives the filled/tonal/outlined/text variants cleanly.)

### Phase 5 — Cheap high-value page fixes

- **`UI/Controls/Banner.xaml` (new `ContentView`)** with bindable `Title`/`Message`/`Icon`/`Stroke`/`Background`/`TextColor` (or a `BannerType` enum mapping to palette tokens). Replace the 4 repeated banner blocks in `MainPage.xaml` (§3.2) and reuse in `TagUploadConsentPopup.xaml`.
- **Remove hardcoded colors** (§8.2): `Crimson` (Settings), `White` text on consent button, `Colors.Green`/`Colors.Red` in `MainPage`'s `BoolToColorConverter` → `Tertiary`/`Error` tokens.
- **`UI/Logs/LogsPage.xaml:10`** title bug: `BambuMan Settings` → `BambuMan Logs` (§5.1).
- **Promote shared converters** (`NoNullToBoolConverter`, `LogLevelToColorConverter`, `BoolToColorConverter`) from `MainPage` page resources to `App.xaml` (§8.5).
- **Inventory chip contrast** (§3.3): outlined `MaterialChip` with a leading color dot (or compute a luminance-based on-color) instead of hardcoded white text on the filament color.
- **Settings save Snackbar** (§4.6): use **CommunityToolkit.Maui `Snackbar`** (already referenced) to show "Settings saved" (debounced) after a commit — no need for HorusStudio's snackbar.
- **Section headers** in Settings (§4.1) — include if low-effort (plain `LabelMedium` headers grouping Connection / Data sharing / Defaults / Behavior / Display).

### Phase 6 — Remove UraniumUI & simplify theming

- Delete UraniumUI package refs (Phase 0 list) and confirm **zero** `UraniumUI` references remain (grep the solution).
- Gut `Resources/Styles/Override.xaml` — its only content is UraniumUI input-field styling; remove it (or repurpose for `MaterialTextField` implicit styles if a global tweak is needed) and drop it from `App.xaml` if emptied.
- `App.xaml.cs`: remove `RequestedThemeChanged += OnRequestedThemeChanged` and the theme-driven `ScheduleShellRecreation` path (the UraniumUI #660 workaround). **Keep** `MaterialDesignControls.InitializeComponents()` and the Glide/#12513 `OnWindowStopped`/`OnWindowResumed` icon-clear + recreate workaround (still using `FontImageSource`). **Verify** light/dark switches live without recreation; if HorusStudio/native controls don't live-update, retain a minimal recreation.

---

## New control: `MaterialAutoCompleteField`

`src/BambuMan/UI/Controls/MaterialAutoCompleteField.xaml(.cs)` — a `ContentView` composing a `MaterialTextField` with a floating suggestion dropdown.

- **Bindable properties:** `Text` (two-way string — the field value, free-text capable), `ItemsSource` (`IEnumerable<string>` of known locations), `Label`/`Placeholder`, `LeadingIcon`, optional `TrailingIcon` + `TrailingIconCommand` (for Settings' refresh).
- **Behavior:** on `Text` change, filter `ItemsSource` (case-insensitive `StartsWith`/`Contains`) into an internal `ObservableCollection`; show a `Border` + `CollectionView` overlay when focused and there are matches; tapping a suggestion sets `Text` and hides the list. Because `Text` is just the field value, the user can type a **new** location not in the list — preserving the current UraniumUI parity.
- **Caution:** overlay positioning inside the MainPage `ScrollView` — anchor the dropdown in a `Grid`/`AbsoluteLayout` so it floats over following content rather than pushing layout. Reuse the existing location `ItemsSource` bindings from the two ViewModels.

---

## Files to modify (primary)

| File | Change |
|---|---|
| `src/BambuMan/BambuMan.csproj` | `<UseMaterial3>true`; drop 3 UraniumUI packages (Phase 6) |
| `src/BambuMan/MauiProgram.cs` | Drop UraniumUI init + icon fonts; add Material Symbols font; full HorusStudio theme wiring |
| `src/BambuMan/App.xaml` | Remove UraniumUI `StyleResource` + xmlns |
| `src/BambuMan/App.xaml.cs` | Remove theme-recreation workaround; keep Glide/#12513 |
| `src/BambuMan/AppShell.xaml` | Flyout → bottom `TabBar`; icon xmlns swap |
| `src/BambuMan/Resources/Styles/Colors.xaml` | Regenerate M3 palette (light+dark) |
| `src/BambuMan/Resources/Styles/Styles.xaml` | Border/Shadow/Button defaults; M3 type scale |
| `src/BambuMan/Resources/Styles/Override.xaml` | Gut UraniumUI input styles |
| `src/BambuMan/Resources/Fonts/MaterialSymbols.ttf` | New self-hosted font |
| `src/BambuMan/Resources/Icons/MaterialSharp.cs` | New generated glyph constants |
| `src/BambuMan/UI/Main/MainPage.xaml` | Controls→HorusStudio; icons; banners; chips; converters |
| `src/BambuMan/UI/Settings/SettingsPage.xaml` | Controls→HorusStudio; switches; autocomplete; Crimson→variants; snackbar |
| `src/BambuMan/UI/Logs/LogsPage.xaml` | Title fix; FA→Material; drop toolbar Home |
| `src/BambuMan/UI/Consent/TagUploadConsentPopup.xaml` | Banner reuse; button hierarchy; icon xmlns |
| `src/BambuMan/UI/Controls/NullableDatePickerField.cs` | **Delete** (→ `MaterialDatePicker`) |
| `src/BambuMan/UI/Controls/Banner.xaml(.cs)` | **New** reusable banner |
| `src/BambuMan/UI/Controls/MaterialAutoCompleteField.xaml(.cs)` | **New** custom autocomplete |
| `src/BambuMan/Platforms/Android/Resources/values/colors.xml` | Match new seed tokens |

## Deferred to a follow-up (the larger per-page UX adds)

MainPage FAB (§3.6), empty state (§3.7), status-pill → inline status-row redesign (§3.1), inline logs-preview cap/CollectionView (§3.8); LogsPage structured rows + level badges + search/filter chips + empty state (§5.2–5.5); ScanPage back-button/hint-overlay/torch/reticle (§6); `BindableLayout`→`CollectionView` sweep (§8.4) where non-trivial.

## Verification

1. **Build:** `dotnet build src/BambuMan/BambuMan.csproj -f net10.0-android` (Release and Debug). Confirm no UraniumUI restore.
2. **No UraniumUI left:** grep the solution for `UraniumUI` → expect zero hits.
3. **Run on Android** (emulator/device via the `/run` skill or `dotnet build -t:Run`) and check:
   - App launches; **bottom navigation** switches Main/Settings/Logs in one tap.
   - All **icons render** (no missing-glyph boxes) on tabs, fields, banners, consent.
   - **Light ⇄ dark**: toggle the system theme — colors update live; brand brown is coherent across both themes (no lavender mismatch).
   - **Inputs:** `MaterialTextField` editing, numeric keyboards, clear buttons; `MaterialDatePicker` opens, accepts and clears (null) a date; native `Switch` toggles persist.
   - **Autocomplete:** typing filters suggestions; selecting fills the field; typing a brand-new location is accepted and saved.
   - **Settings save** shows the snackbar; QR scan + URL test still work.
4. **Tests:** `dotnet test src/BambuMan.sln` (Shared + SpoolMan.Api) — confirm the migration didn't break shared logic.