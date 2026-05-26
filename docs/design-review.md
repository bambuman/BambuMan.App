# BambuMan MAUI App — Design Review

A page-by-page review against Material 3 (Material You) standards, with concrete XAML changes you can paste in. Findings reference exact file paths and line numbers in `src/BambuMan/`.

The app already uses UraniumUI Material, MaterialSymbols, and a `StyleResource` wired up in `App.xaml` — so the foundations to go fully Material 3 are in place. Most fixes are about pruning inconsistencies, removing hardcoded colors, and reorganizing dense screens into M3 patterns (banner, list item, segmented buttons, navigation bar).

---

## 1. Priorities (start here)

If you only do five things, do these — they fix the most jarring inconsistencies and unlock the rest of the M3 system.

1. **Pick one accent and derive the dark theme from it.** Today `Primary` is brown (`#7E570E`) but `PrimaryDark` is purple (`#ac99ea`), and `SurfaceTint` is yet another purple (`#6750A4`). The app reads as two different products in light and dark mode. Generate a proper M3 tonal palette from a single seed color (use [m3.material.io/theme-builder](https://m3.material.io/theme-builder/) with seed `#7E570E`) and replace `Colors.xaml` wholesale.
2. **Replace the flyout with a bottom Navigation Bar.** Three destinations (Main, Settings, Logs) is too few for a flyout drawer and too many to hide. M3 Navigation Bar with 3 items is the recommended pattern below 5 destinations. The `TabBar` block in `AppShell.xaml` (lines 43–56) is already commented out — uncomment and remove the flyout.
3. **Standardize banner/card/button shapes.** All borders are `RoundRectangle 5,5,5,5`; M3 wants 12dp for cards, 8dp for chips, full (pill) for buttons under 40dp height. Plus the default `Border` style in `Styles.xaml` (line 16) sets `StrokeShape="Rectangle"` — overriding M3 rounded corners everywhere.
4. **Enforce 48dp minimum touch targets.** "Clear inventory", "Clear logs", "Email logs", and the status pills are all 20–26dp tall. Below the WCAG 2.1 / M3 minimum and hard to tap reliably.
5. **Extract banner and section-header into reusable controls.** `MainPage.xaml` repeats the same Error / Success / Info / Version banner pattern four times (lines 43–124). One `Banner` `ContentView` would shrink the file by ~80 lines and make the dark-mode story consistent.

---

## 2. Foundation issues (Colors, Styles, Override, AppShell)

### 2.1 Color palette is inconsistent across themes

`Resources/Styles/Colors.xaml`

The light-mode brand color is warm brown gold; the dark-mode brand color is cool lavender. They have no tonal relationship, so the brand identity collapses when the user switches themes. Other anomalies:

- `SurfaceTint` (line 83) is `#6750A4` — Material's default purple seed, not the app's brown.
- `Secondary` is a desaturated brown but `SecondaryDark` is `#CCC2DC` (lavender). Same disconnect.
- `Background` and `Surface` are both `#FFFBFE` — M3 expects a slight tonal lift between them so cards read as elevated.
- `StatusBar` (`#EADFDF`) is a pinkish neutral that matches neither the brand nor the surface family.
- Several "semantic" colors (banners, secondary/tertiary buttons) are hand-mixed and don't track the rest of the palette.

**Recommendation.** Regenerate the palette from a single seed and standardize on the M3 role names you already use (`Primary`, `OnPrimary`, `PrimaryContainer`, `Surface`, `SurfaceContainer`, etc.). Replace the eight banner color blocks (Error/Success/Info/Version/Warning/Caution) with derivations from M3 roles:

```xml
<!-- Instead of bespoke ErrorBanner* colors, use existing M3 error tokens -->
<!-- Error banner = ErrorContainer fill + Error stroke + OnErrorContainer text -->
<!-- Success banner = a tertiary-green generated from seed, used only here -->
```

Eliminate the `Crimson`, `DodgerBlue`, `Yellow*Accent`, `Cyan*Accent`, `Blue*Accent` blocks (lines 220–228) — none of them are referenced by anything sensible, and `Crimson` is hardcoded directly in `SettingsPage.xaml` (see §4.2).

### 2.2 Default `Border` style overrides M3 shape

`Resources/Styles/Styles.xaml` line 16–20:

```xml
<Style TargetType="Border">
    <Setter Property="Stroke" Value="..." />
    <Setter Property="StrokeShape" Value="Rectangle"/>   <!-- problem -->
    <Setter Property="StrokeThickness" Value="1"/>
</Style>
```

Setting `StrokeShape="Rectangle"` as a global default means every `Border` in the app is sharp-cornered unless overridden — and `MainPage.xaml` alone has to override it on 10 borders. Remove this setter and let each Border declare its shape, or change the default to `RoundRectangle 12` which matches M3 card shape.

### 2.3 Drop shadow is unrealistic

`Resources/Styles/Styles.xaml` line 267–272:

```xml
<Style TargetType="Shadow">
    <Setter Property="Radius" Value="15" />
    <Setter Property="Opacity" Value="0.5" />
    <Setter Property="Brush" Value="{AppThemeBinding Light=White, Dark=White}" />
    <Setter Property="Offset" Value="10,10" />
</Style>
```

White shadows with a 10×10 offset look like a hard 1990s drop-shadow, not M3 elevation. M3 elevation uses a 4–8dp Y-offset, 0 X-offset, black at 12–20% opacity, and 8–16dp blur radius. Fix:

```xml
<Style TargetType="Shadow">
    <Setter Property="Radius" Value="12" />
    <Setter Property="Opacity" Value="0.15" />
    <Setter Property="Brush" Value="Black" />
    <Setter Property="Offset" Value="0,4" />
</Style>
```

### 2.4 Buttons miss M3 size + state

`Resources/Styles/Styles.xaml` lines 26–46:

```xml
<Setter Property="CornerRadius" Value="8"/>
<Setter Property="Padding" Value="14,10"/>
```

`Padding="14,10"` produces a 36–40dp tall button — below M3's 40dp filled-button minimum and well under the 48dp touch-target minimum. Move to `Padding="24,10"` and set `MinimumHeightRequest="40"` (M3 calls this `Tonal/Filled Button = 40dp`). For full-pill chrome (the modern M3 default since 2024 Expressive update), set `CornerRadius="20"`.

Also missing: `Pressed` and `Focused` visual states. Buttons only have `Normal` and `Disabled` defined.

### 2.5 Flyout vs Bottom Navigation

`AppShell.xaml` uses `FlyoutBehavior="Flyout"` for three destinations. M3 guidance:

> Use a **Navigation Bar** (bottom) for 3–5 top-level destinations. Use a **Navigation Drawer** only for 6+ destinations or when destinations don't need to be one tap away.

You already commented out a TabBar block (lines 43–56) — uncomment it, remove the flyout config, and the app gains one-tap access to Home/Settings/Logs plus an M3 bottom bar look. The `Settings` toolbar item on `MainPage` (line 22) and the `Home` toolbar items on `SettingsPage`/`LogsPage` become redundant and should be removed — they currently double up with the flyout.

### 2.6 Flyout item template is non-standard

If you keep the flyout, the current template (lines 18–25) uses a 0.2*/0.8* grid and a 35dp image. M3 Navigation Drawer rows are 56dp tall with a 24dp leading icon and `BodyLarge` label. Replace with:

```xml
<Shell.ItemTemplate>
    <DataTemplate>
        <Grid ColumnDefinitions="56,*" HeightRequest="56" Padding="12,0">
            <Image Source="{Binding FlyoutIcon}" HeightRequest="24" WidthRequest="24"
                   VerticalOptions="Center" HorizontalOptions="Center" />
            <Label Grid.Column="1" Text="{Binding Title}"
                   VerticalTextAlignment="Center" FontSize="16" />
        </Grid>
    </DataTemplate>
</Shell.ItemTemplate>
```

---

## 3. MainPage (`UI/Main/MainPage.xaml`)

The home screen is the densest in the app: status indicators, banners, inventory chips, the spool-edit form, a toggle card, and an optional logs preview — all stacked in a `VerticalStackLayout` inside a `ScrollView`. It does a lot, and most of the visual debt lives here.

### 3.1 Status pills look tappable but aren't

Lines 49–85. Three pill-shaped borders showing `SETTINGS`, `SPOOLMAN`, `NFC ENABLED` status. They have rounded corners, outline strokes, and a green/red dot — every visual cue says "button". But they're inert. Worse, they're 26dp tall, below the touch-target minimum, so even if you wanted to add tap targets they'd fail accessibility.

**Recommendation.** Either:
- Make them tappable. Tapping `SETTINGS` (red) navigates to Settings, `SPOOLMAN` (red) opens the URL field, `NFC` (red) launches the Android NFC settings intent. Then raise height to 48dp.
- Or replace with an M3 inline status row using `Material Symbols` `check_circle` / `error` icons inline with text. No pill chrome, no false affordance.

The latter feels right for an info-only display:

```xml
<HorizontalStackLayout Spacing="16" Padding="4,8">
    <HorizontalStackLayout Spacing="6">
        <Image HeightRequest="16" Source="{FontImageSource ...
            Glyph={x:Static m:MaterialSharp.Check_circle},
            Color={Binding SettingsOk, Converter=...}}" />
        <Label Text="Settings" FontSize="14"/>
    </HorizontalStackLayout>
    <!-- Spoolman, NFC the same -->
</HorizontalStackLayout>
```

### 3.2 Four identical banner blocks — extract one component

Lines 43–124 implement the Version, Error, Success, and Info banners as nearly-identical `Border` + `Label` blocks. Each ~12 lines.

Create `UI/Controls/Banner.xaml` (`ContentView`):

```xml
<ContentView x:DataType="controls:Banner" x:Name="Root">
    <Border IsVisible="{Binding Source={x:Reference Root}, Path=IsVisible}"
            StrokeThickness="1" StrokeShape="RoundRectangle 12"
            Padding="12,8" Margin="0,0,0,8"
            Stroke="{Binding Stroke}" BackgroundColor="{Binding Background}">
        <Grid ColumnDefinitions="Auto,*" ColumnSpacing="8">
            <Image Source="{Binding Icon}" HeightRequest="20"
                   IsVisible="{Binding Icon, Converter={StaticResource NoNullToBoolConverter}}"/>
            <Label Grid.Column="1" TextColor="{Binding TextColor}">
                <Label.FormattedText>
                    <FormattedString>
                        <Span Text="{Binding Title}" FontAttributes="Bold"/>
                        <Span Text=" "/>
                        <Span Text="{Binding Message}"/>
                    </FormattedString>
                </Label.FormattedText>
            </Label>
        </Grid>
    </Border>
</ContentView>
```

Then `MainPage.xaml` lines 43–124 collapse to four `<controls:Banner …/>` lines. Plus the consent popup's three banner blocks (§6.4) reuse the same control.

### 3.3 Inventory chips can fail contrast

Lines 135–151. The chip background is `{Binding Color}` (the actual filament color), and label text is hardcoded `TextColor="White"`. Bambu white, beige, neon yellow, and pastel filaments all fail WCAG AA against white text.

Either compute a contrasting on-color in the ViewModel (luminance-based pick of white or black), or wrap the chip in an outlined M3 chip style (`Stroke=Outline`, `BackgroundColor=Transparent`, color shown as a leading 12dp dot):

```xml
<Border StrokeShape="RoundRectangle 8" Stroke="{StaticResource OutlineVariant}"
        StrokeThickness="1" Padding="8,4">
    <HorizontalStackLayout Spacing="6">
        <Ellipse Fill="{Binding Color}" HeightRequest="10" WidthRequest="10"/>
        <Label Text="{Binding Material}" FontSize="13"/>
        <Label Text="×" FontSize="13" Opacity="0.6"/>
        <Label Text="{Binding Quantity}" FontSize="13"/>
    </HorizontalStackLayout>
</Border>
```

This is the modern M3 input-chip / assist-chip pattern, and reads cleanly against any filament color.

### 3.4 Inline ad-hoc colors

Line 208: `Background="{AppThemeBinding Light=#F7F4F6, Dark=#2C2A30}"` is a one-off neutral the toggle card uses. Move to `Colors.xaml` as `SurfaceContainer` / `SurfaceContainerDark` (which is a standard M3 role anyway).

### 3.5 Cancel/Save button hierarchy

Lines 191–193. Both buttons are filled — Cancel uses `TertiaryButton*` gray fill, Save uses `FilledButton`. M3 hierarchy:

| Action | Style |
|---|---|
| Primary positive (Save) | `FilledButton` (already correct) |
| Secondary (Cancel) | `TextButton` or `OutlinedButton`, not another filled button |

```xml
<Button Grid.Column="0" Grid.Row="3" StyleClass="OutlinedButton"
        Text="Cancel" Clicked="CloseButton_OnClicked"/>
<Button Grid.Column="1" Grid.Row="3" StyleClass="FilledButton"
        Text="Save changes" Clicked="SaveChanges_OnClicked"/>
```

Drop the `SecondaryButton*` and `TertiaryButton*` color blocks from `Colors.xaml` entirely once you adopt M3 button style classes (filled / tonal / outlined / text).

### 3.6 Add a Floating Action Button for "Read tag"

Reading a tag is the central action of the app, but the page has no primary call to action — the user discovers it by tapping their phone to the NFC tag. An M3 extended FAB at the bottom-right would (a) advertise the gesture and (b) provide a manual fallback. Use `material:Fab` from UraniumUI or a `Button` styled as a 56dp circle anchored to the bottom-right:

```xml
<Grid> <!-- replace the outer ScrollView with this Grid wrapper -->
    <ScrollView>...</ScrollView>
    <Button Text="Ready to scan" StyleClass="FilledButton" CornerRadius="28"
            HeightRequest="56" Padding="20,0"
            ImageSource="{FontImageSource Glyph={x:Static m:MaterialSharp.Nfc}, ...}"
            HorizontalOptions="End" VerticalOptions="End"
            Margin="0,0,16,16"/>
</Grid>
```

When `NfcIsEnabled` is false, the FAB switches to "Enable NFC" and deep-links to settings. Big UX win.

### 3.7 Empty state

When the page loads without a spool and without inventory items, the area between status pills and the toggle card is blank. Add an empty-state illustration + microcopy:

> *(Material Symbols `contactless` 96dp)*
> **Hold your phone to a Bambu spool**
> The tag will appear here automatically.

Standard M3 empty-state pattern — icon, headline (TitleMedium), supporting text (BodyMedium), centered with 32dp vertical margins.

### 3.8 Logs preview duplicates LogsPage

Lines 220–242 render a logs list inline. This is the same data shown on LogsPage. Currently the inline version has no scroll cap and grows unbounded inside a ScrollView (perf and layout problems). Cap at the last 5 entries and add a "View all" tail link:

```xml
<Label Text="View all logs →" Style="{StaticResource Link}"
       HorizontalOptions="End"
       Tapped="ViewAllLogs_OnTapped"/>
```

Or remove this entirely and rely on the bottom-nav Logs tab once §2.5 lands.

---

## 4. SettingsPage (`UI/Settings/SettingsPage.xaml`)

Settings is a flat list of inputs with no grouping. With 11 distinct settings, the user has to scan the whole page to find anything.

### 4.1 Add M3 section headers

Group settings under headers that match how a user thinks about them:

| Group | Settings |
|---|---|
| **Connection** | Spoolman URL (+ Test, Scan QR) |
| **Data sharing** | Participate in NFC upload + info |
| **Defaults applied on read** | Buy date, Price, Lot nr, Location |
| **Behavior** | Import unknown filament, Show keyboard on read, Override location on read |
| **Display** | Show logs on main page |

M3 section header pattern: 14sp, `Primary` color, all-caps, 16dp top padding:

```xml
<Label Text="CONNECTION" FontSize="14" FontAttributes="Bold"
       TextColor="{AppThemeBinding Light={StaticResource Primary}, Dark={StaticResource PrimaryDark}}"
       Margin="0,16,0,8"/>
```

### 4.2 Hardcoded Crimson buttons

Lines 38–44 ("Scan QR") and lines 79–86 ("Refresh"). Both use:

```xml
BackgroundColor="Transparent"
TextColor="Crimson"
BorderColor="Crimson"
```

Crimson (`#DC143C`) is unrelated to the brand and breaks in dark mode. Replace with `StyleClass="OutlinedButton"` and let the M3 style classes drive the color:

```xml
<Button Grid.Column="1" StyleClass="OutlinedButton"
        ImageSource="{FontImageSource Glyph={x:Static m:MaterialSharp.Qr_code_scanner}, ...}"
        Clicked="ImageButton_OnClicked"/>
```

Better yet — these two buttons are *trailing icons on a text field*, not adjacent buttons. UraniumUI's `material:TextField` supports `<material:TextField.Attachments>` for trailing icon-buttons inside the field's border. That eliminates the awkward "button glued onto a field" layout entirely.

### 4.3 Inconsistent checkbox margins

Lines 48, 56, 58, 60, 62 each use a slightly different margin (`10,5,0,0` vs `10,0,10,0` vs `10,0,10,5`). Fix to a single value and add supporting text underneath each checkbox label — M3 list-item pattern is icon + headline + supporting text on a 56–72dp row:

```xml
<Grid ColumnDefinitions="*,Auto" Padding="0,12">
    <VerticalStackLayout Grid.Column="0" Spacing="2">
        <Label Text="Participate in NFC data upload" FontSize="16"/>
        <Label Text="Help build the public Bambu RFID library."
               FontSize="14" Opacity="0.7"/>
    </VerticalStackLayout>
    <Switch Grid.Column="1" IsToggled="{Binding FullTagScanAndUpload}"/>
</Grid>
```

(Switch + label is more M3 than CheckBox + label for settings — checkboxes are for multi-select lists; switches are for on/off settings.)

### 4.4 "Back to Main" duplicates the toolbar Home

Lines 18–25 add a Home toolbar item, and lines 98–102 add a second "Back to Main" button at the bottom of the page. Once you adopt the bottom Navigation Bar (§2.5), both go away. Until then, drop the bottom button — it's a navigation duplicate.

### 4.5 "Test Spoolman Url" is buried below the form

Line 92–96. Testing the URL is the only feedback the user has that the connection works, but it's an outlined button below the entire form. Move it inline as a trailing icon on the Spoolman URL field (`material:TextField.Attachments`), or display the connection status (✓ Connected / ✗ Failed) as supporting text below the field after each edit.

### 4.6 Auto-save without indication

The page commits each change reactively (via TwoWay binding) but never tells the user it has saved. M3 pattern is a transient snackbar:

> ✓ Settings saved

Use `CommunityToolkit.Maui.Alerts.Snackbar` after each commit (debounced 500ms).

---

## 5. LogsPage (`UI/Logs/LogsPage.xaml`)

### 5.1 Wrong page title

Line 10: `Title="BambuMan Settings"`. Copy-paste bug — should be `BambuMan Logs`.

### 5.2 No structure inside log entries

Lines 47–52 render each log as a single `Label` of `{Binding Content}`. No timestamp, no level badge, no expandable detail. With 100+ entries it's a wall of colored text.

M3 list item:

```xml
<Grid ColumnDefinitions="Auto,*" ColumnSpacing="12" Padding="16,8">
    <Border Grid.Column="0" StrokeShape="RoundRectangle 4" Padding="6,2"
            BackgroundColor="{Binding Level, Converter={StaticResource LogLevelToBgConverter}}">
        <Label Text="{Binding Level}" FontSize="11" FontAttributes="Bold"
               TextColor="White"/>
    </Border>
    <VerticalStackLayout Grid.Column="1" Spacing="2">
        <Label Text="{Binding Content}" FontSize="14"/>
        <Label Text="{Binding Timestamp, StringFormat='{0:HH:mm:ss}'}"
               FontSize="12" Opacity="0.6"/>
    </VerticalStackLayout>
</Grid>
```

### 5.3 Use `CollectionView`, not `BindableLayout` in a `StackLayout`

Both this page and `MainPage`'s log preview use `BindableLayout` over a `StackLayout`. No virtualization — every log row stays in the visual tree even when off-screen. Use `CollectionView` so MAUI recycles cells:

```xml
<CollectionView ItemsSource="{Binding Logs}" >
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:LogModel">
            <!-- log row -->
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

### 5.4 Missing filter / search

Add an M3 `SearchBar` at the top of the list, and chips for level filtering (Errors / Warnings / Info / All). Logs are the first place a user goes when something breaks — being able to filter to errors is essential.

### 5.5 Empty state

When `Logs` is empty: Material Symbols `inbox` icon + "No logs yet" headline + "Activity will appear here as you scan tags."

---

## 6. ScanPage (`UI/Scan/ScanPage.xaml`)

This is the most undercooked screen — only 26 lines.

### 6.1 Back button is invisible

Lines 20–22:

```xml
<Button x:Name="BackButton" Grid.Column="0" WidthRequest="50" HeightRequest="50"
        CornerRadius="25" Clicked="BackButton_Clicked"/>
```

No text, no icon, default brown background. Against a camera viewfinder it's a brown circle of unknown purpose. Fix:

```xml
<Button x:Name="BackButton" WidthRequest="48" HeightRequest="48" CornerRadius="24"
        BackgroundColor="#99000000" TextColor="White"
        ImageSource="{FontImageSource FontFamily=MaterialSharp,
                                      Glyph={x:Static m:MaterialSharp.Close},
                                      Color=White, Size=24}"
        Margin="16" Clicked="BackButton_Clicked"
        HorizontalOptions="Start" VerticalOptions="Start"/>
```

Semi-transparent black for visibility over any background, white close icon, top-left anchor, 16dp safe-area margin.

### 6.2 No scanning hint

The user sees a camera feed with no instruction. Add an overlay:

```xml
<Frame BackgroundColor="#99000000" CornerRadius="12" Padding="16"
       HorizontalOptions="Center" VerticalOptions="End" Margin="32,0,32,80">
    <Label Text="Point the camera at the QR code" TextColor="White"
           FontSize="15" HorizontalTextAlignment="Center"/>
</Frame>
```

### 6.3 Add a torch toggle

`BarcodeScanning.Native.Maui` exposes `TorchOn` on `CameraView`. Add a top-right icon button (`flash_on` / `flash_off`) — essential for low-light scanning.

### 6.4 Visual scan target

Even with `AimMode="True"`, an explicit reticle ($\square$ corners around a 240×240 area, animated pulse) makes the scanning experience feel intentional. Pure SVG/Border overlay, no library needed.

---

## 7. TagUploadConsentPopup (`UI/Consent/TagUploadConsentPopup.xaml`)

The strongest screen in the app — clear hierarchy, semantic icons, distinct warning/info banner styles. Small polish items:

### 7.1 Hardcoded `WidthRequest="340"`

Line 10. On small phones (320dp wide) this clips against the edges. Drop the fixed width and let the popup use its container's layout — or set `MaximumWidthRequest="400"` instead.

### 7.2 Root padding

`Padding="0"` (line 10) on the outer `VerticalStackLayout` puts content flush against the popup edges. M3 dialog content padding is 24dp. Add `Padding="24"` and reduce inner `Margin="0,8,0,0"` accordingly.

### 7.3 Title icon size

A 38dp icon next to a 20pt label is M2-era. M3 hero-dialog pattern is a 24dp icon centered *above* the headline:

```xml
<VerticalStackLayout HorizontalOptions="Center" Spacing="16">
    <Image HeightRequest="24" Source="{FontImageSource Glyph={x:Static m:MaterialSharp.Science}, ...}"/>
    <Label Text="Research Data Contribution" FontSize="24" FontAttributes="Bold"
           HorizontalTextAlignment="Center"/>
</VerticalStackLayout>
```

### 7.4 Button hierarchy

Lines 154–165. "No Thanks" uses a gray fill, "Participate" uses Primary fill. Two filled buttons compete — make the decline a `TextButton`:

```xml
<Button StyleClass="TextButton" Text="No thanks" Clicked="OnDeclineClicked"/>
<Button StyleClass="FilledButton" Text="Participate" Clicked="OnAcceptClicked"/>
```

M3 dialog confirmation pattern is left-aligned text-button + right-aligned filled-button, separated by a spacer.

---

## 8. Cross-cutting recommendations

### 8.1 Typography scale

`FontSize` is set inline on dozens of labels across the XAML files (`12`, `13`, `14`, `15`, `20` are the ad-hoc values, plus `14` baked into the default `Styles.xaml` styles). There's no semantic scale — visually similar text uses different sizes from page to page. Define the M3 type scale once in `Styles.xaml`:

```xml
<Style x:Key="DisplayMedium" TargetType="Label">
    <Setter Property="FontSize" Value="28"/>
    <Setter Property="LineHeight" Value="36"/>
</Style>
<Style x:Key="HeadlineMedium" TargetType="Label">
    <Setter Property="FontSize" Value="24"/>
    <Setter Property="FontAttributes" Value="Bold"/>
</Style>
<Style x:Key="TitleLarge" TargetType="Label">
    <Setter Property="FontSize" Value="22"/>
</Style>
<Style x:Key="BodyLarge" TargetType="Label">
    <Setter Property="FontSize" Value="16"/>
</Style>
<Style x:Key="BodyMedium" TargetType="Label">
    <Setter Property="FontSize" Value="14"/>
</Style>
<Style x:Key="LabelMedium" TargetType="Label">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="FontAttributes" Value="Bold"/>
</Style>
```

Then every label references one of six names — consistent, theme-able, easy to scale later for accessibility.

### 8.2 Replace hardcoded hex with tokens

Direct hex literals appear in:
- `MainPage.xaml` line 208 (`#F7F4F6` / `#2C2A30`)
- `SettingsPage.xaml` lines 38–44 and 79–86 (`Crimson`)
- `TagUploadConsentPopup.xaml` line 163 (`White`)
- And the `BoolToColorConverter` in `MainPage.xaml` line 34 (`Colors.Green` / `Colors.Red`)

The converter should map to `Tertiary` (success) and `Error` from the palette, not `Colors.Green`/`Colors.Red`.

### 8.3 Accessibility quick-wins

- Set `SemanticProperties.Description` on every icon-only button (the 50×50 back button on ScanPage, the info button on Settings).
- Raise `HeightRequest="20"` / `"26"` / `"30"` buttons to 48dp.
- Make sure all `FontImageSource` icons that convey state (the status pill dots, the chip color dots) are paired with text — they already are; just verify on new components.

### 8.4 Use `CollectionView` consistently

`BindableLayout` over a `StackLayout` is used for Inventory chips (MainPage line 135), Logs preview (line 233), and LogsPage logs (line 45). Convert all three to `CollectionView` with appropriate `ItemsLayout` (`HorizontalLinearItemsLayout` for chips, `VerticalLinearItemsLayout` for logs).

### 8.5 Page Resources cleanup

`MainPage.xaml` declares converters in `ContentPage.Resources` (lines 32–38). They're page-local copies of converters that other pages also use. Promote `NoNullToBoolConverter`, `LogLevelToColorConverter`, and `BoolToColorConverter` to `App.xaml` resources so every page can use them without re-declaring.

---

## 9. Suggested phasing

| Phase | Scope | Effort |
|---|---|---|
| **1. Foundation** | New M3 palette (§2.1), Border/Shadow defaults (§2.2–2.3), Button sizes + state (§2.4), Typography scale (§8.1) | ~1 day, repo-wide diff |
| **2. Navigation** | Bottom Navigation Bar (§2.5), remove duplicate toolbar items (§4.4), flyout template (§2.6) | ~half day |
| **3. Reusable controls** | `Banner` ContentView (§3.2), `SectionHeader` style (§4.1), trailing-icon TextField helpers (§4.2, §4.5) | ~1 day |
| **4. MainPage** | Status row (§3.1), chips (§3.3), Cancel/Save hierarchy (§3.5), FAB (§3.6), empty state (§3.7), logs cap (§3.8) | ~1 day |
| **5. SettingsPage** | Section grouping (§4.1), Crimson removal (§4.2), checkbox→switch (§4.3), inline test (§4.5), snackbar (§4.6) | ~half day |
| **6. LogsPage** | Title fix (§5.1), structured rows (§5.2), CollectionView (§5.3), filter (§5.4), empty (§5.5) | ~half day |
| **7. ScanPage** | Back button (§6.1), hint overlay (§6.2), torch (§6.3), reticle (§6.4) | ~half day |
| **8. Polish** | Consent popup tweaks (§7), hardcoded color sweep (§8.2), accessibility audit (§8.3), CollectionView sweep (§8.4) | ~1 day |

Roughly a week's work to get to a consistent, expressive Material 3 baseline. Phases 1 and 2 are blockers for everything else; the rest can land independently.
