# Runway placement and runway-free sites — 2026-10-03

## Investigation

MAT1 workspace `4e9c5df6aa7d4869` is a real runway-free build. Its recorded Package Tool result has exit code 0, successful package validation and four layout entries. Its generated FSData contains the stock BF_TemplateHouse01 SceneryObject at the saved placement and the lodge foundation polygon; no Airport is present. The later `55f8a25c8df0462b` build retains that scenery and adds Airport, Runway and TaxiwayParking. These files were read only. No simulator or installed Community package was changed for this investigation.

This establishes successful generation/compilation, not live building visibility. If Matt means missing from airport search, omission of Airport explains it. If buildings were physically absent, that remains a separate live scenery issue requiring reproduction; it is not proof that standalone scenery requires a runway.

Microsoft's 2024 Runway XML reference explicitly says airports cannot be defined without at least one runway. Independent FSData SceneryObjects do not depend on that Airport record. Parking plans remain saved before runway construction but do not provide spawn points. No helipad workaround or automatic fake runway has been introduced.

References:
- https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Airports_And_Facilities/Runway_XML_Properties.htm
- https://docs.flightsimulator.com/msfs2024/html/2_DevMode/Scenery_Editor/Objects/Runway_Objects.htm

Transparency only hides runway material, not terraforming. The SDK documents automatic flattening and profile/falloff controls, but no supported no-terraform switch in this runway interface. An explicit Hide runway surface option is available, labelled with the terrain limitation. Existing templates add no runway markings or lights. Placement currently has no economic charge; no new cost or economy mechanism was added. Live compiler/simulator acceptance of the transparency option remains pending.

## Editing

Open My Sites → Edit selected site → Draw runway. Click the first end and then the opposite end. A first-end marker and pointer preview appear; Escape cancels without changing the saved runway. Draw again to relocate it. Change heading, length, width and centre east/north fields for precise adjustment; numeric heading/offsets commit when focus leaves the box. Save, validate, rebuild and install through the existing protected workflow, then restart MSFS.

The avatar heading remains the initial default only. Map placement and numeric edits are independent. New optional persisted local east/north runway offsets default to zero for legacy sites. The surveyed WGS84 anchor/elevation/provenance remains intact. Runway geometry, XML, map rendering/fitting, parking clearance, taxiway snapping, site-envelope validation and airport recognition all use the same offsets. Existing object/parking local positions stay unchanged. New default objects/stands are placed relative to the moved runway. Layout exports carry runway placement metadata and reject incompatible imports.

## Verification

Automated tests cover two-click placement, cancellation, geometry editing, restart persistence, transparency XML, independent scenery/parking positions, moved clearance and recognition, and legacy zero offsets. Isolated WPF smoke uses the Draw runway button and actual heading textbox. No unattended Package Tool or live MSFS test was run.

Verification completed: 594 full solution tests passed; focused layout/site/scenery tests passed; Release build had 0 warnings and 0 errors; WPF site-edit/drawing/heading-input smoke passed and screenshot reviewed. Self-contained Windows app republished and Desktop shortcut refreshed. Live MSFS visibility, transparent-surface and moved-runway terrain acceptance remain pending.
