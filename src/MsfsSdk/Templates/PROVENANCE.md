# Single-runway template provenance

Frontier Pilot maintains the template in `SingleRunway/`. Its structure and placeholder assets derive from Microsoft's MSFS 2024 SDK **1.2.4**, not independently authored scenery or artwork. The original SDK sample is never edited or accessed by the generator; the template is embedded in the MsfsSdk assembly.

## Source and baseline

- Installed SDK: `C:\Users\mattr\Desktop\MSFS 2024 SDK`.
- Original source: `Samples\DevmodeProjects\Sceneries\SimpleAirport`.
- Proven compatibility baseline: repository `workspaces/diagnosis-20260906-130640/SimpleAirport`, recorded in `evidence/diagnosis-20260906-130640`.
- This baseline already contained the required `<PackageOrderHint>CUSTOM_AIRPORT</PackageOrderHint>`. The unchanged old SDK sample did not pass current simulator package validation without it.
- Source file SHA-256 values: `provenance-source-hashes.json` alongside this document. The earlier successful baseline and installed source were rechecked unchanged in `evidence/prototype-0.1/preservation-check.json`.

## Changes made by Frontier

Retain the proven project version 2, package structure, `Copy`/`BGL`/`MaterialLib` asset groups and relative paths. Retain one Airport, the first Runway, and its PRIMARY/SECONDARY RunwayStart children. Remove the second runway, TaxiName, DeleteAirport, runway deformation, coloration, markings, lights, approach lights and VASI. Optional country/city/traffic settings were removed. No object placements, aprons, parking or other infrastructure are included.

The static template is the successfully compiled FPT1 output from the generator, with one flat runway at the first sample runway's centre and altitude. It is itself a usable working project. Airport and package names are Frontier-owned (`FPT1`, `frontier-airport-fpt1`, creator `Frontier Pilot`). The generator rewrites the variable XML fields through System.Xml.Linq and assigns a stable material GUID derived from SHA-256 of `FrontierPilot/Asphalt/{IDENTIFIER}`; it does not reuse the SDK material GUID. Material library output paths are package-specific.

The proven asphalt material settings, two source textures, their `.FLAGS` files and ContentInfo thumbnail are retained as dependencies. The material's name/GUID are changed, but its shader settings and texture pixels are not. These are **Microsoft/Asobo sample assets**, not original Frontier artwork. The SDK's accompanying `MSFS SDK EULA.pdf` is preserved beside this notice; no new license is asserted over those assets. They are prototype placeholders with their provenance retained.

## Required metadata

`PackageOrderHint` must be `CUSTOM_AIRPORT` for this airport template. Do not remove it because the Package Tool returns zero: on simulator 1.8.16.0 a missing value produces validation errors and omits layout.json despite exit 0.

## Physical and naming conventions

- The example uses the first runway centre: latitude `25.035986003223783`, longitude `-77.46942558230664`.
- Flat airport, runway and starts use `4.1148001316736` metres (the sample runway altitude `13.500000431999998F` converted with 0.3048).
- Length `2529.3122612096568` m and width `46.85408462689318` m also retain that runway's dimensions.
- The original airport reference point and unequal endpoint elevations are not retained: Prototype 0.1 uses its runway centre as the airport reference and one explicitly supplied flat altitude. No terrain sampling or flattening is implied.
- True heading `89.64241865029217°`; explicit east-positive magnetic variation `-6°` from the sample (XML west-positive magvar `6`). Runway pair **10/28**. No magnetic-model lookup is performed.
- Starts are at the calculated geodesic endpoints, facing inward along the same centreline. They are not the original sample's slightly inset start positions.

References: [MSFS Runway XML](https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Airports_And_Facilities/Runway_XML_Properties.htm), [Airport XML](https://docs.flightsimulator.com/msfs2024/html/5_Content_Configuration/Environment/Airports_And_Facilities/Airport_XML_Properties.htm), [Package XML](https://docs.flightsimulator.com/msfs2024/html/8_SDK_Tools/Package_Tool/Package_Tool_XML_Properties.htm).
