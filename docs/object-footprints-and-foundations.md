# True-scale object footprints and terrain foundations

## Measured model metadata

The installed `Gen_Hangar56` package contains compiled glTF payloads. Frontier Pilot's catalogue dimensions come from the highest-detail LOD0 payload in the read-only `generic.BGL`, not from a simulator screenshot. The evidence parser read every mesh POSITION accessor bound and applied the glTF scene's node translation, rotation, scale and hierarchy before taking the union.

LOD0 model-local bounds in metres:

- X/right: -25.4537354785 to 20.7289423559; width 46.1826778344 m.
- Y/up: 0.0000038527 to 10.8669685271; height 10.8669646744 m.
- Z/forward: -49.3580607910 to 45.0466633301; depth 94.4047241211 m.
- Footprint centre relative to placement origin: -2.3623965613 m right and -2.1556987305 m forward.

LOD1 and LOD2 independently agree within about 0.31 m in width and 0.11 m in depth. LOD0 is authoritative because it contains the complete visible geometry. The inspected BGL SHA-256 remains `0185952D0B0D73C231DBD8C0DE65404F4B86FC32FE58664DC6D91AED93D261D3`.

`SceneryFootprint` stores the four model-local bounds rather than only width/depth. This preserves an off-centre placement origin. `SceneryObjectGeometry` rotates each bound corner around the MSFS origin using the placed object's true heading and then translates it into the existing local east/north metre frame. The canvas sends those metre points through the same `LayoutViewport` as runways, parking and taxiways, with no minimum on-screen object size. The origin is a dot; the filled yellow polygon is the physical model bound.

## Foundation generation

The asset catalogue associates `Gen_Hangar56` with a `TerrainFoundation`: 2 m of flat margin beyond each model bound and 8 m of feathered falloff. The canvas shows the flat boundary dashed and the affected falloff boundary dotted.

For each placed instance, generation adds a four-vertex `<Polygon>` sibling before its `<SceneryObject>`. Its vertices are the same rotated footprint expanded by 2 m and converted to WGS84. It uses the official MSFS 2024 polygon attributes:

- `FlattenMode=0` enables flattening (the SDK's value is counterintuitive).
- `ForceElevation=1` makes the interior level.
- `ExcludeSecondaryHeightmap=1` removes local secondary-heightmap roughness.
- `FlattenFalloff=8` feathers the height transition outside the small flat polygon.

The polygon elevation is the airport's guarded survey ground elevation, the same construction datum already used by the proven runway. The scenery object remains AGL zero with `snapToGround=TRUE`, so it is placed on the resulting terrain. This is suitable for buildings close to the surveyed airport datum. A future workflow needs a separately validated per-object elevation survey for structures placed on a different broad terrace or grade; Frontier Pilot does not invent one in this milestone. Microsoft documents polygon altitude only as metres, so live testing remains the acceptance gate for datum behaviour on this simulator build.

Official references:

- https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Scenery_Objects/Scenery_XML_Properties.htm
- https://docs.flightsimulator.com/msfs2024/html/2_DevMode/Scenery_Editor/Objects/Polygon_Objects.htm

SDK 1.2.4 accepted the generated polygon/object project with no builder errors and produced a real BGL. This proves syntax and compilation, not its visual terrain result. See `evidence/object-foundations/`.
