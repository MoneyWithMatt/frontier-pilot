# Scenery Materials & Prototype Buildings V0

## Read-only SDK findings recorded before implementation

The installed SDK is `C:\Users\mattr\Desktop\MSFS 2024 SDK`, version **1.2.4**. Its `SimpleAirport` sample is under `Samples\DevmodeProjects\Sceneries\SimpleAirport`. These installed files were inspected read-only.

The sample does not reference a stock runway material by a portable library GUID. It packages `myasphalt.material` (SHA-256 `B494081172A13521BFDD9590384D0144730E68FF600948385D3B464C84BF1DE8`) and two local textures in a `MaterialLib` asset group, then references that package-local material GUID from each `Runway`. The current Frontier generator inherited that structure. Legacy Asphalt retains the sample-derived material settings/textures; legacy Dirt retains the already-proven Frontier procedural albedo. Those files are project dependencies rather than a catalogue of stock scenery materials.

The SDK 1.2.4 XML reference describes `Runway.surface` as a material name and documents standard surface identifiers including `ASPHALT`, `DIRT`, `GRASS`, `SHORT_GRASS`, `GRAVEL` and `SAND`. It also permits ordered child `Material` layers with a GUID, blend type, U/V tiling, width, falloff and opacity. Runway objects are baked into terrain textures. The installed KALO sample contains built-in string references such as `ASPHALT`, but its many GUID references are not identified as a stable public stock-material catalogue.

A disposable Package Tool matrix replaced the generated runway's local material GUID with each of `DIRT`, `SAND`, `GRAVEL`, `GRASS`, `SHORT_GRASS` and `ASPHALT`. All six builds returned exit code 0 and produced their package `layout.json`. The generated source, results and build output live only under `workspaces/surface-native-probe-20260910`; the concise result is preserved in `evidence/scenery-materials-v0/native-surface-probe.json`. This proves compiler acceptance on SDK 1.2.4. It does not prove visual colour, texture, blending or seasonal behaviour in MSFS.

The installed `SimpleScenery` sample demonstrates package-owned models with terrain-snapped `SceneryObject` elements containing `LibraryObject` GUIDs (its `objects.xml` SHA-256 is `E13DB18ACD3D050EBD9C6C026B9B04DD301C821A07D73625764C1D13BBF03F0A`). Frontier already emits the same XML shape for external library references. The existing installed-content investigation established Asobo `Gen_Hangar56` at `{FD8AA200-1485-46C7-BA8F-B9B69C31E0F7}` and measured its transformed LOD0 footprint as 46.1827 by 94.4047 metres. Matt has visually accepted its position and orientation. Referencing it copies no stock model into the Frontier package, but availability still depends on the corresponding simulator content package.

## V0 catalogue choices

The surface catalogue uses stable Frontier preset IDs and explicit availability. Compacted Dirt, Sandy Dirt, Gravel, Green Grass and Modern Asphalt use the compiler-proven native identifiers. Dry Grass temporarily falls back to `GRASS`, because neither the SDK sample nor the public surface identifier distinguishes a dry colour. Weathered Asphalt temporarily falls back to `ASPHALT`, because no stable public weathered-material identifier was established. These fallbacks are labelled in data and UI; no texture artwork is invented.

Existing definitions with no preset ID retain their prior generated appearance byte-for-byte: the legacy `Dirt` enum selects the procedural dirt material and legacy `Asphalt` selects the sample-derived asphalt material. Newly edited definitions save a stable preset ID.

The construction catalogue contains the eight requested stable categories. Only **Prototype Base Lodge placeholder** is placeable in V0. It references the installed base-scenery `BF_TemplateHouse01` model at normal scale so the temporary visual is an actual standalone house. This replaces `MICROSOFT_ASSET_FieldCabins_01`, which live testing showed as a large compound/hangar-like object despite its catalogue name. The 10 × 8 m authoring footprint is provisional until live model bounds are measured. The stable Frontier asset ID is unchanged, so rebuilding an existing site updates an already-placed lodge without changing saved layout data. The other categories remain explicitly unavailable until a reliable asset reference or Frontier-owned model exists.

## Visual acceptance still required

In MSFS, check each preset's actual colour and texture, grass density/seasonal response, edge blending and taxiway consistency. Also check that the prototype lodge resolves as one small house, is oriented correctly and sits naturally on its local foundation. Its 10 × 8 m editor footprint is an authoring estimate until the model is measured from readable source data or accepted live. Compiler success is not visual verification.

Official references inspected: [Runway XML Properties](https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Airports_And_Facilities/Runway_XML_Properties.htm), [Shared Environment XML Properties](https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Shared_XML_Properties.htm), [Runway Objects](https://docs.flightsimulator.com/msfs2024/html/2_DevMode/Scenery_Editor/Objects/Runway_Objects.htm), and [Scenery Objects](https://docs.flightsimulator.com/msfs2024/html/2_DevMode/Scenery_Editor/Objects/Scenery_Objects.htm).
