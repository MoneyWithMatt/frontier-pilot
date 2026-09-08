# Generic scenery-object placement proof

## Selected library object

The first deliberately small catalogue entry is `asobo-generic-hangar-56`, displayed as **Generic hangar (Gen_Hangar56)**. MSFS identifies the model with library GUID `{FD8AA200-1485-46C7-BA8F-B9B69C31E0F7}`. Generated scenery references that GUID with `<LibraryObject name="{...}" scale="1"/>`; no Microsoft model or texture is copied into a Frontier project.

The GUID/name pair was recovered from the locally installed Asobo generic-airport model library. The GUID's binary form is also indexed in the current MSFS 2024 streamed package `fs24-asobo-modellib-airport-generic` (`minimal.fsarchive` SHA-256 `CA95E12F4DC728954EEE78930B1976ABAB1090FCB0A7716AEC9848FA44A12A1E`). The older readable `generic.BGL` used to resolve the name/GUID pair has SHA-256 `0185952D0B0D73C231DBD8C0DE65404F4B86FC32FE58664DC6D91AED93D261D3`. These files were inspected read-only.

Official MSFS 2024 scenery XML documentation defines `SceneryObject` and its `LibraryObject` child, and says the `name` value is the model GUID shown in the Scenery Editor Objects list:

- https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Scenery_Objects/Scenery_XML_Properties.htm
- https://docs.flightsimulator.com/msfs2024/html/2_DevMode/Scenery_Editor/Objects/Scenery_Objects.htm

## Frontier representation and generation

`PlacedSceneryObject` stores an instance number, Frontier asset ID, east/north metre offsets from the guarded survey anchor, and true heading. `SceneryAssets` currently resolves one Frontier asset ID to the MSFS GUID and preview dimensions. This keeps saved definitions independent of screen pixels and gives later Frontier-owned assets a stable local identifier without introducing a full catalogue.

The object uses `AirportSite`, WGS84 local-to-geographic conversion and heading normalization shared with existing infrastructure. The XML generator emits each object as an `FSData` sibling of `Airport`, ordered by instance number, with its calculated latitude/longitude, normalized true heading, scale 1, AGL altitude zero and ground snapping. An edit invalidates prior validation, build and install authority.

Saved-airport JSON and layout exports include `PlacedObjects`. Missing `PlacedObjects` remains compatible with older documents and means an empty collection. My Airports still reopens at Survey Captured and requires validation and a fresh build.

## UI test

1. Open or create an airport with a captured survey.
2. In **Airport layout**, choose **Add test hangar**. A yellow `H1` rectangle appears near the runway.
3. Drag it on the metre-based layout. Select it to enter exact east/north offsets and true heading, or press `R` / `Shift+R` (or the 15-degree buttons) to rotate it.
4. Use **Delete selected** to remove it. Undo/redo includes add, move, rotation and deletion. Add the test hangar again for multiple instances.
5. Save the airport, close Frontier Pilot, reopen it through My Airports, and confirm the hangar is restored.
6. Confirm the site, validate, generate/compile and install with MSFS closed. Restart MSFS and inspect the object's location, terrain contact and heading.

Automated tests prove local geometry, validation, deterministic XML, persistence, editing/history and workflow invalidation. SDK 1.2.4 also compiled a disposable object-enabled project successfully, with no builder errors and a real BGL. Matt subsequently confirmed that the object appeared at the intended airport and orientation in MSFS. That test exposed an inaccurate 2D footprint and a floating rear edge; Job 4A.1 addresses both in [object-footprints-and-foundations.md](object-footprints-and-foundations.md).
