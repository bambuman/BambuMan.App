# BambuMan.App — Bambuddy Parallel Inventory Backend

## Context

BambuMan.App currently inventories Bambu Lab spools against a Spoolman server. The user wants Bambuddy ([maziggy/bambuddy](https://github.com/maziggy/bambuddy)) added as a **second selectable backend** alongside the existing SpoolMan integration — not a replacement. SpoolMan's `ExternalFilament` library is kept as a metadata-enrichment source for filament matching even when Bambuddy is the active backend.

The match key from a Bambu Lab tag is the 32-hex `tray_uuid` (block 9 of the NFC tag, parsed at [BambuFilamentInfo.cs:220](../BambuMan.Shared/BambuFilamentInfo.cs:220)). Example: `3148AE1E2F6E4668942866C7226FAFA9`.

**No Bambuddy backend dependency for v1.** Phase 1 implements against the current Bambuddy API (no upstream patch needed) by fetching `GET /inventory/spools` and matching `tray_uuid` client-side. Phase 2 swaps in the proposed `GET /spools/by-tag` endpoint (see [bambuddy-by-tag-endpoint.md](bambuddy-by-tag-endpoint.md)) once that PR is merged — a one-line implementation change in `BambuddyManager.FindSpoolByTag`.

## User decisions

- Parallel backends; user picks active one in settings (default: spoolman)
- OpenAPI-generated C# client (mirror `SpoolMan.Api/`)
- Match by `tray_uuid` primary, `tag_uid` fallback
- Reduced edit panel in Bambuddy mode (used-weight, location, note)
- Convert per-spool price → `cost_per_kg = price / (label_weight / 1000)`
- No data migration from SpoolMan → Bambuddy

## Approach

Mirror the SpoolMan integration pattern. Add a generated `BambuMan.Bambuddy.Api` client project, a `BambuddyManager` singleton, settings + UI for backend selection, and a dispatch point at [MainPage.xaml.cs:470](../BambuMan/UI/Main/MainPage.xaml.cs:470). Extract filament-matching logic so both managers share it.

Test server: `http://10.10.10.70:8007` with API key `bb_60jiCiePgkxY4Aa-ZXvB8mJfm0401KE-x3W1jTTPO2g`. OpenAPI spec at `http://10.10.10.70:8007/openapi.json` (829 KB).

---

## 1. New project: `BambuMan.Bambuddy.Api`

Generated at `src/BambuMan.Bambuddy.Api/` from `http://10.10.10.70:8007/openapi.json`, mirroring [SpoolMan.Api/](../SpoolMan.Api/). Use the same openapi-generator-cli invocation documented in [SpoolMan.Api/README.md](../SpoolMan.Api/README.md) — only change `packageName=BambuMan.Bambuddy.Api`, input URL, and target `net10.0`.

**Post-gen patch (one line):** rename `AddApi` extension in `BambuMan.Bambuddy.Api/Extensions/IServiceCollectionExtensions.cs` → `AddBambuddyApi` to avoid clash with SpoolMan.Api's same-named method at call site. Document in a `BambuMan.Bambuddy.Api/README.md` so re-generation is repeatable.

**Add to solution:** new entry in [BambuMan.sln](../BambuMan.sln) + ProjectReference from [BambuMan.csproj](../BambuMan/BambuMan.csproj) (mirror existing SpoolMan.Api reference).

**Auth header:** wire `X-API-Key` in the `AddApiHttpClients(client => ...)` callback (same shape as Basic auth at [SpoolmanManager.cs:160](../BambuMan.Shared/SpoolmanManager.cs:160)).

---

## 2. New class: `BambuddyManager`

Location: `src/BambuMan.Shared/BambuddyManager.cs`. Singleton, mirrors SpoolmanManager's narrow public surface.

**Public surface:**
- Properties: `ApiUrl`, `ApiKey`, `Status` (reuse `SpoolmanManagerStatusType`), `IsHealth`, `IsInitialized`, `UnknownFilamentEnabled`, `OverrideLocationOnRead`, `HasNetworkAccess`, `AppVersion`, `ShowLogs`
- Events: `OnStatusChanged`, `OnShowMessage`, `OnLogMessage`, `OnPlayErrorTone`, `OnSpoolFound` (delivers `BambuddySpoolFound` record — new tiny model in `BambuMan.Shared/Models/` with `Id`, `Material`, `TrayUid`, `Brand`, `ColorName`, `WeightUsed`, `LabelWeight`, `CoreWeight`)
- Methods: `Init()`, `InventorySpool(BambuFilamentInfo, DateTime?, decimal?, string?, string?)`, `UpdateSpoolReduced(int id, double? weightUsed, string? location, string? note)`

**Init flow** (mirror [SpoolmanManager.Init lines 103–237](../BambuMan.Shared/SpoolmanManager.cs:103)):
1. Compose base URL: trim trailing slash, append `/api/v1` if missing
2. Build `IHost` with `AddBambuddyApi`, baseAddress, `X-API-Key` header, 5s timeout, retry(3)
3. Health check: probe a known cheap endpoint (confirm against fetched openapi.json — likely `GET /api/v1/filament-catalog/?limit=1` or a dedicated `/health` if present). 3 attempts with 3s delay (mirror SpoolmanManager pattern at line 181)
4. **Skip** `TryCheckDefaultValuesAsync` (no vendor/extra-field bootstrap in Bambuddy)
5. Reuse `TryLoadLocalFilamentsAsync` via the shared matcher (section 3) — embedded resource only, no Spoolman HTTP dependency
6. Start health-check timer ([SpoolmanManager.cs:1014](../BambuMan.Shared/SpoolmanManager.cs:1014) pattern)

**InventorySpool flow:**
1. `FindSpoolByTag(info.TrayUid)` — see "Find-by-tag implementation" below
2. **Not found → create path:**
   - `ExternalFilamentMatcher.FindExternalFilament(info, ...)` for color/brand enrichment
   - `POST /inventory/spools` with body mapping:
     - `material` ← `info.FilamentType`
     - `subtype` ← `info.DetailedFilamentType`
     - `color_name` ← matched external filament `Name` (fallback `info.Color`)
     - `rgba` ← `info.Color` (already 8-hex)
     - `brand` ← matched external `Manufacturer` or `"Bambu Lab"`
     - `label_weight` ← `info.SpoolWeight ?? 1000`
     - `core_weight` ← `250`
     - `nozzle_temp_min/max` ← `info.MinTemperatureForHotend / MaxTemperatureForHotend`
     - `storage_location` ← `location` param
     - `cost_per_kg` ← `price.HasValue ? price.Value / (info.SpoolWeight ?? 1000) * 1000m : null`
     - `data_origin` ← `"nfc_scan"`, `tag_type` ← `"bambu_rfid"`
   - `PATCH /inventory/spools/{id}/link-tag` body `{tag_uid: info.SerialNumber, tray_uuid: info.TrayUid}`. Bambuddy normalizes server-side per [`tag_normalization.py`](../../../bambuddy/backend/app/utils/tag_normalization.py) — no client-side normalization needed.
3. **Found → update path:**
   - If `OverrideLocationOnRead` and `location` differs: `PATCH /inventory/spools/{id}` with `storage_location`
   - Fire `OnSpoolFound`
4. Wrap in `HttpRequestException` / `TaskCanceledException` handlers identical to [SpoolmanManager.HandleNetworkError](../BambuMan.Shared/SpoolmanManager.cs:1096)

### Find-by-tag implementation

**Phase 1 (current Bambuddy API — no upstream changes):**
- At `Init()`, kick off `LoadSpoolsInBackground` — same shape as [SpoolmanManager.cs:474](../BambuMan.Shared/SpoolmanManager.cs:474). `GET /api/v1/inventory/spools?include_archived=false`, cache to `cachedSpools : List<Spool>` field.
- `FindSpoolByTag(trayUid)` scans cache: `cachedSpools.FirstOrDefault(s => string.Equals(s.TrayUuid, trayUid, StringComparison.OrdinalIgnoreCase))`. Fall back to `tag_uid` match if no `tray_uuid` hit.
- After a successful `POST /inventory/spools` + `PATCH /link-tag`, append the new spool to the cache (don't re-fetch the whole list).
- Refresh the cache opportunistically: on `Init()`, and on health-check timer tick if it's been > 5 minutes since last refresh.
- Practical limit: comfortable up to ~500 spools. Typical Bambu Lab user has 5–50, so this is fine.

**Phase 2 (after `GET /spools/by-tag` PR lands upstream):**
- Replace cache scan with direct API call: `GET /inventory/spools/by-tag?tray_uuid={trayUid}` → 200 with `Spool`, 404 → null.
- Drop the `LoadSpoolsInBackground` call and `cachedSpools` field.
- Regenerate `BambuMan.Bambuddy.Api` from the updated openapi.json. The generated `IInventoryApi` will gain a `GetInventorySpoolsByTagAsync(...)` method automatically.
- Single-method change in `BambuddyManager.FindSpoolByTag` — no other code touched.

**Do not** inherit from SpoolmanManager — composition only. Most of SpoolmanManager's 1200 lines (vendors, locations, extra fields, full Spool editing) is Spoolman-specific.

---

## 3. Shared refactor: `ExternalFilamentMatcher`

User wants SpoolMan's `ExternalFilament` library used as metadata enrichment even when Bambuddy is active. The embedded `BambuMan.Shared/Resources/filaments.json` already contains the full Bambu Lab catalog — no Spoolman HTTP dependency needed.

**Extract** from [SpoolmanManager.cs](../BambuMan.Shared/SpoolmanManager.cs) into new `src/BambuMan.Shared/ExternalFilamentMatcher.cs`:
- `GenerateUnknownFilament` (line 1162)
- `ExtendWithMissingFilaments` (line 1180) — change to return fresh list, not mutate
- `FindExternalFilament` (line 666–835) — keep as a static method taking `(BambuFilamentInfo, IList<ExternalFilament>, bool unknownFilamentEnabled)`

SpoolmanManager keeps an instance wrapper delegating to it (pure refactor — no behavior change for SpoolMan users). BambuddyManager calls the static directly with the embedded list.

Embedded JSON loader: extract `LoadLocalFilaments` ([SpoolmanManager.cs:428](../BambuMan.Shared/SpoolmanManager.cs:428)) into the matcher as a static `LoadEmbeddedFilaments() : IList<ExternalFilament>`.

---

## 4. Settings + DI

**Preference keys** added at [SettingsPage.xaml.cs:18–27](../BambuMan/UI/Settings/SettingsPage.xaml.cs:18):

```csharp
public const string KeyInventoryBackend = "inventory_backend";  // "spoolman" | "bambuddy"
public const string KeyBambuddyUrl = "bambuddy_url";
public const string KeyBambuddyApiKey = "bambuddy_api_key";
```

**SettingsPageViewModel** ([SettingsPageViewModel.cs](../BambuMan/UI/Settings/SettingsPageViewModel.cs)): add `[ObservableProperty]` fields `InventoryBackend` (string, default `"spoolman"`), `BambuddyUrl`, `BambuddyApiKey`.

**SettingsPage.xaml** ([SettingsPage.xaml:27–104](../BambuMan/UI/Settings/SettingsPage.xaml:27)): add a `RadioButton` or `SegmentedControl` at top selecting backend, then two collapsible sections (existing Spoolman section + new Bambuddy section with URL + API-key entries). Bind section visibility to `InventoryBackend`. Add a `TestBambuddyUrl_OnClicked` button mirroring [`TestSpoolmanUrl_OnClicked`](../BambuMan/UI/Settings/SettingsPage.xaml.cs:227) doing a health-probe `GET` with `X-API-Key`.

**DI** ([MauiProgram.cs:105–111](../BambuMan/MauiProgram.cs:105)):

```csharp
services.AddSingleton<BambuddyManager>();
```

**AppShell redirect** ([AppShell.xaml.cs](../BambuMan/AppShell.xaml.cs)): "if active backend's URL is empty, redirect to Settings" — replace the existing `spoolman_url`-only check.

---

## 5. NFC flow dispatch

[MainPage.xaml.cs](../BambuMan/UI/Main/MainPage.xaml.cs) currently constructor-injects only `SpoolmanManager`. Add `BambuddyManager` to the constructor. At [line 469–470](../BambuMan/UI/Main/MainPage.xaml.cs:469):

```csharp
var backend = Preferences.Default.Get(SettingsPage.KeyInventoryBackend, "spoolman");
SentrySdk.ConfigureScope(s => s.SetTag("inventory.backend", backend));

await viewModel.ClearMessages();
if (backend == "bambuddy")
    await bambuddyManager.InventorySpool(bambuFilamentInfo, buyDate, defaultPrice, defaultLotNr, defaultLocation);
else
    await spoolmanManager.InventorySpool(bambuFilamentInfo, buyDate, defaultPrice, defaultLotNr, defaultLocation);
```

**Event subscription block** ([MainPage.xaml.cs:182–223](../BambuMan/UI/Main/MainPage.xaml.cs:182)): subscribe to both managers' events. Add `BambuddyManagerOnSpoolFound` handler that opens the reduced edit panel (section 6).

**InitializeSpoolmanAsync** ([MainPage.xaml.cs:225](../BambuMan/UI/Main/MainPage.xaml.cs:225)): rename → `InitializeBackendsAsync`, init both managers. Each fails fast on missing URL ([SpoolmanManager.cs:107](../BambuMan.Shared/SpoolmanManager.cs:107)), no retry pressure on inactive backend.

---

## 6. Reduced edit panel (Bambuddy mode)

Out of scope to mirror the full SpoolMan edit panel ([MainPage.xaml.cs:661–694](../BambuMan/UI/Main/MainPage.xaml.cs:661)). For Bambuddy mode show a simplified panel with three fields: `weight_used` (current consumption), `storage_location`, `note`. Save calls `BambuddyManager.UpdateSpoolReduced` → `PATCH /inventory/spools/{id}` with only those three fields.

XAML: add a second `DataTemplate` or a `ContentView` swapped in via `DataTrigger` on backend mode. Reuse existing styling.

---

## 7. Verification

**Client manual smoke against http://10.10.10.70:8007 (Phase 1):**
1. Settings → switch backend to Bambuddy → enter URL + API key → "Test Bambuddy URL" → expect success toast
2. Confirm initial cache load: app should `GET /inventory/spools` once on Init and populate the in-memory cache
3. Scan a fresh tag → cache miss on tray_uuid, expect 201 on POST `/inventory/spools`, 200 on PATCH `/link-tag`. New spool appended to local cache. Verify in Bambuddy web UI at http://10.10.10.70:8007/
4. Re-scan same tag → cache hit, no POST issued, `OnSpoolFound` fires, inventory counter increments
4. Toggle "Override location on read", set new location, re-scan → expect PATCH `/spools/{id}` with new `storage_location`
5. Open reduced edit panel → change `weight_used` and `note` → save → verify in Bambuddy UI
6. Switch backend back to Spoolman → confirm SpoolMan flow still works (regression check)
7. Disable network → expect health-check timer trips, status flips to `CantConnectToApi`, reconnects when network returns

**Unit tests** in `src/BambuMan.Shared.Test/`:
- `BambuddyManagerTests.InventorySpool_NewTag_CreatesAndLinks` — mock generated `IInventoryApi`, assert cache miss → create → link-tag → cache-append sequence
- `BambuddyManagerTests.InventorySpool_ExistingTagInCache_FiresOnSpoolFound` — pre-populate cache, assert no POST/PATCH issued
- `BambuddyManagerTests.FindSpoolByTag_FallsBackToTagUidWhenTrayUuidNoMatch` — verify the tray_uuid → tag_uid fallback order
- `BambuddyManagerTests.PriceConversion` — verify `cost_per_kg = price / (label_weight / 1000) * 1000`
- `ExternalFilamentMatcherTests` — port existing FindExternalFilament coverage if present

## Critical files

- New: `src/BambuMan.Bambuddy.Api/` (generated project)
- New: [src/BambuMan.Shared/BambuddyManager.cs](../BambuMan.Shared/BambuddyManager.cs)
- New: [src/BambuMan.Shared/ExternalFilamentMatcher.cs](../BambuMan.Shared/ExternalFilamentMatcher.cs)
- New: `src/BambuMan.Shared/Models/BambuddySpoolFound.cs`
- Edit: [src/BambuMan.Shared/SpoolmanManager.cs](../BambuMan.Shared/SpoolmanManager.cs) (delegate matching to shared matcher; no behavior change)
- Edit: [src/BambuMan/MauiProgram.cs:105](../BambuMan/MauiProgram.cs:105)
- Edit: [src/BambuMan/UI/Main/MainPage.xaml.cs](../BambuMan/UI/Main/MainPage.xaml.cs) (constructor, event subscriptions, dispatch, reduced edit panel handler)
- Edit: [src/BambuMan/UI/Settings/SettingsPage.xaml](../BambuMan/UI/Settings/SettingsPage.xaml) + `.cs` (preference keys, backend selector, Bambuddy section)
- Edit: [src/BambuMan/UI/Settings/SettingsPageViewModel.cs](../BambuMan/UI/Settings/SettingsPageViewModel.cs) (new observable properties)
- Edit: [src/BambuMan/AppShell.xaml.cs](../BambuMan/AppShell.xaml.cs) (backend-aware redirect)
- Edit: [src/BambuMan.sln](../BambuMan.sln) (new project entry)

## Out of scope (v1)

- Full edit panel parity for Bambuddy
- Data migration SpoolMan ↔ Bambuddy
- Spool archive / k_profiles / shopping list / assignments endpoints
- Bambuddy filament-catalog management from inside BambuMan.App (read-only consumption only — catalog managed in Bambuddy web UI)

## Phase 2 follow-up

After [bambuddy-by-tag-endpoint.md](bambuddy-by-tag-endpoint.md) lands upstream:

1. `git pull` Bambuddy fork, redeploy, confirm `GET /api/v1/inventory/spools/by-tag` reflects in `/openapi.json`
2. Regenerate `BambuMan.Bambuddy.Api/` from the updated spec
3. Replace `BambuddyManager.FindSpoolByTag` body — drop cache scan, call `inventoryApi.GetInventorySpoolsByTagAsync(trayUuid, tagUid)` directly. Map 404 → null.
4. Remove `cachedSpools` field, `LoadSpoolsInBackground`, and the cache-refresh tick on the health timer.
5. Remove cache-append-on-create logic in `InventorySpool`.
6. Re-run the manual smoke checklist — same expected behavior, fewer network calls.
