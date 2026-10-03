# Site editing and dark navigation — 2026-10-02

## Changes

The main sections now use a persistent left sidebar with a shared dark palette, compact typography and consistent controls. Site creation/editing remains inside My Sites. Existing cargo, economy and tracking domain workflows are unchanged.

Saved-site editing restores the existing survey and layout without showing new-site connection/capture controls. Those setup commands also refuse to run against an active saved site, preventing accidental layout clearing. Map fitting waits for actual WPF measurement; previously a pre-measurement fit could make restored objects too small to see. Editing scrolls directly to the restored layout and displays object counts.

Runway creation is an explicit Draw runway map tool rather than a checkbox. Click the two runway ends to set its independent centre, heading and length. Heading, length, width and centre east/north offsets remain editable. The surveyed site anchor, buildings and parking do not move. It remains a straight runway, not an arbitrary taxiway polyline. Remove runway preserves stands and taxiways as plans. Parking and taxiways can be authored before a runway without fabricating one. Clearance against the runway becomes applicable when the runway is present.

MSFS limitation: TaxiwayParking is airport infrastructure and an Airport requires a runway. The existing generator therefore omits airport infrastructure when no runway exists. Planned stands persist but do not create MSFS spawn points until the airfield is built. No automatic hidden runway is generated. An explicit Hide runway surface option uses the documented transparent attribute; this hides pavement, but does not disable MSFS runway flattening.

## Editing an existing site

1. Open My Sites, select the saved site, and choose Edit selected site.
2. Confirm the editing title and restored building/parking counts. Use Fit view if needed.
3. Select and drag existing objects; edit their properties in the map panel. Add parking before or after placing a runway.
4. Use Draw runway and click both endpoints when ready to add airfield infrastructure. Resolve parking clearance conflicts if needed.
5. Save site updates the same saved identity. Validate, rebuild and install through the existing ownership-protected workflow to update MSFS scenery.

## Verification

- 60 focused layout/site/scenery tests passed.
- Complete solution: 581 tests passed.
- Release solution build: zero warnings and errors.
- Isolated WPF SiteUiSmoke exercises the real Edit button and verifies restored lodge/parking, navigation, hidden recapture controls, removed checkbox and measured map fitting. Rendered primary pages reviewed at 1280 x 860.
- No Package Tool invocation or live MSFS scenery validation performed. Map tiles may not be populated in immediate offline smoke captures; the existing provider is retained.
- Existing LoadingWpfSmoke passed: fractional payload entry, focus retention, fuel draft retention and zero reset. Self-contained Windows publication completed and the existing Desktop shortcut was refreshed.
