# Bambuddy — Add `GET /inventory/spools/by-tag` Endpoint

## Context

[maziggy/bambuddy](https://github.com/maziggy/bambuddy) is a FastAPI/SQLAlchemy app that manages 3D-printer filament inventory. The `Spool` model already has `tag_uid` (16 hex, NFC chip UID) and `tray_uuid` (32 hex, Bambu Lab spool UUID). The helper `get_spool_by_tag()` at [`backend/app/services/spool_tag_matcher.py:339`](https://github.com/maziggy/bambuddy/blob/main/backend/app/services/spool_tag_matcher.py#L339) already implements the lookup logic (tries `tray_uuid` first, falls back to `tag_uid`, only returns active/non-archived spools).

This helper is currently only callable from `POST /spoolbuddy/nfc/tag-scanned` (designed for hardware NFC reader devices — broadcasts a WebSocket event, returns `{matched, spool_id}`).

**Gap:** there is no plain REST `GET` endpoint for "look up spool by tag" that a regular client can call. A consumer wanting this lookup today has to either:
- `GET /api/v1/inventory/spools` and filter client-side (inefficient — full table fetch on every NFC scan), or
- Impersonate a SpoolBuddy device and parse the WebSocket-coupled response (not a clean API)

## Proposal

Add one read-only REST endpoint that exposes the existing helper:

```
GET /api/v1/inventory/spools/by-tag
    Query: tray_uuid: str | None   (32 hex, optional)
           tag_uid:   str | None   (16 hex, optional)
    Auth:  X-API-Key, permission INVENTORY_READ
    200 → SpoolResponse (existing schema, includes k_profiles via selectinload)
    404 → {"detail": "Spool not found"} when both args provided/normalized but no active spool matches
    422 → when both args missing or empty after normalization
```

**No new business logic** — purely a REST surface for the existing helper. The active-spool filter (`Spool.archived_at.is_(None)`), tray_uuid-over-tag_uid priority, zero-UID rejection (`ZERO_TRAY_UUID`, `ZERO_TAG_UID`), and case-normalization are inherited from `get_spool_by_tag()`.

## Why this approach

- **Single source of truth:** delegates to the existing helper — both `/spoolbuddy/nfc/tag-scanned` and the new REST endpoint return identical results.
- **No schema changes:** uses existing `SpoolResponse` + existing columns.
- **No new permissions:** `INVENTORY_READ` already gates similar endpoints (e.g. `GET /inventory/spools/{id}` at line 940).
- **Composable:** clients compose find→create themselves via existing `POST /inventory/spools` + `PATCH /spools/{id}/link-tag`. No need for a `find-or-create` endpoint — server-side composition would force decisions about which fields to default from where and complicate idempotency.

## Implementation

### File: `backend/app/api/routes/inventory.py`

Add near the existing tag-related route at line 1482 (`link-tag`). Pseudocode:

```python
from backend.app.services.spool_tag_matcher import get_spool_by_tag

@router.get("/spools/by-tag", response_model=SpoolResponse)
async def find_spool_by_tag(
    tray_uuid: str | None = None,
    tag_uid: str | None = None,
    db: AsyncSession = Depends(get_db),
    _: User | None = RequirePermissionIfAuthEnabled(Permission.INVENTORY_READ),
):
    """Look up an active spool by Bambu Lab tray UUID or RFID tag UID.

    Prefers tray_uuid (more reliable for Bambu Lab spools), falls back to
    tag_uid. Returns 404 when no active spool matches, 422 when neither
    identifier is provided. Archived spools are excluded.
    """
    if not tray_uuid and not tag_uid:
        raise HTTPException(422, "At least one of tray_uuid or tag_uid is required")

    spool = await get_spool_by_tag(db, tag_uid or "", tray_uuid or "")
    if not spool:
        raise HTTPException(404, "Spool not found")
    return spool
```

### Tag normalization

The helper internally calls `normalize_tag_uid` / `normalize_tray_uuid` from [`backend/app/utils/tag_normalization.py`](https://github.com/maziggy/bambuddy/blob/main/backend/app/utils/tag_normalization.py) — clients can send the raw 32/16-hex string without worrying about case or stripping. Document this in the docstring.

### OpenAPI tag

Inherits the router's `tags=["inventory"]`. The generated OpenAPI spec at `/openapi.json` will pick it up automatically — no manual schema edits needed.

## Tests

Add to `backend/tests/api/routes/test_inventory.py` (or wherever existing `link-tag` tests live — locate via `grep -r "link-tag" backend/tests/`):

1. `test_find_spool_by_tray_uuid_returns_200` — seed a spool with `tray_uuid="3148AE1E2F6E4668942866C7226FAFA9"`, `GET /spools/by-tag?tray_uuid=3148AE1E2F6E4668942866C7226FAFA9` → 200 with full spool.
2. `test_find_spool_by_tag_uid_returns_200` — seed with `tag_uid="3148AE1E2F6E4668"`, `GET /spools/by-tag?tag_uid=3148ae1e2f6e4668` (lowercase) → 200 (case normalization).
3. `test_find_spool_prefers_tray_uuid_over_tag_uid` — seed two spools, one matches `tray_uuid` and a different one matches `tag_uid`; query with both → returns the `tray_uuid` match.
4. `test_find_spool_returns_404_for_unknown_tag` — `GET /spools/by-tag?tray_uuid=00000000000000000000000000000001` → 404.
5. `test_find_spool_returns_422_with_no_args` — `GET /spools/by-tag` → 422.
6. `test_find_spool_excludes_archived` — archive a matching spool; query → 404.
7. `test_find_spool_requires_inventory_read` — call without API key (auth enabled) → 401/403.

## Verification

Manual smoke against a dev Bambuddy instance:

```bash
# Seed a spool first via existing endpoints, then:
curl -H "X-API-Key: $KEY" \
  "http://localhost:8007/api/v1/inventory/spools/by-tag?tray_uuid=3148AE1E2F6E4668942866C7226FAFA9"
# → 200 with SpoolResponse JSON

curl -H "X-API-Key: $KEY" \
  "http://localhost:8007/api/v1/inventory/spools/by-tag?tray_uuid=DEADBEEFDEADBEEFDEADBEEFDEADBEEF"
# → 404 {"detail":"Spool not found"}

curl -H "X-API-Key: $KEY" \
  "http://localhost:8007/api/v1/inventory/spools/by-tag"
# → 422
```

## PR notes

- **Scope:** one route addition + tests. No model, schema, or migration changes.
- **Backwards compatibility:** purely additive. No existing endpoints touched.
- **Performance:** the underlying helper queries on indexed `tray_uuid` / `tag_uid` columns with `limit(1)`. Single round-trip, no full-table scan.
- **Motivation paragraph for the PR description:** "External clients (mobile apps, scripts) that want to check whether an NFC tag is already inventoried currently have to fetch `GET /inventory/spools` and filter client-side, or impersonate a SpoolBuddy hardware device. This adds a thin REST endpoint that reuses the existing `get_spool_by_tag()` helper, giving regular API consumers the same lookup capability with one indexed query."

## Critical file

- Edit: `backend/app/api/routes/inventory.py` (add route near line 1482)
- Edit: `backend/tests/api/routes/test_inventory.py` (or wherever `link-tag` tests live — find via grep)

## Out of scope for this PR

- `POST /spools/find-or-create` (client composes this from existing endpoints)
- Bulk-by-tag lookup
- Lookup of archived spools (intentional — matches helper behavior)
- WebSocket broadcast on lookup (this is a query, not a state change)
