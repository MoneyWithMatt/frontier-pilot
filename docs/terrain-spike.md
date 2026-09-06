# Arbitrary-location terrain spike — 6 September 2026

**Outcome: elevation gate failed. No Skeleton Coast airport generated or installed.** The console probe successfully connected to the running simulator, but the installed MSFS 2024 SDK 1.2.4 has no `SimConnect_RequestGroundInfo` declaration or DLL export. No supported direct latitude/longitude terrain query was identified in the current official MSFS documentation reviewed. This is a capability limitation, not a failed SimConnect connection.

## Evidence and scope

- [Final probe](../evidence/terrain-spike/probe-02.json): native library loaded by absolute path, library/header SHA256 recorded, successful Open handshake, successful Sim system-state request, successful read-only user-aircraft data request, clean connection close.
- [Build log](../evidence/terrain-spike/build-final.log): .NET 8 console, zero warnings/errors. No external NuGet dependencies, no copied SDK binaries.
- [Initial probe](../evidence/terrain-spike/probe-01.json): independently confirmed absent export and working connection before adding the local SimVar read.
- [Preservation check](../evidence/terrain-spike/preservation.json): all 13 installed FPT1 files match the prior installation hashes. No Community package was written, moved or deleted by this spike. Installed SDK/sample and baseline source were read only.

The simulator was initially closed and was launched through Steam (`steam://rungameid/2537590`) for this test. The probe received application name `SunRise`, protocol application version `12.2.282174.999`; this is the handshake value, not the marketing/file version (recorded separately in preservation.json). MSFS remains running. No aircraft position was changed by the probe.

The local sample returned latitude approximately -0.000000216, longitude 90.000322773, and ground altitude 5.218339907 metres. These values are recorded only as a protocol/data-path observation: the flight/scenery state was not visually verified, and the sample is **not accepted as a surveyed terrain elevation**. It says nothing about Namibia. Sim system-state value 1 alone does not establish that terrain has finished loading.

## Official documentation reviewed

Accessed 6 September 2026:

1. [MSFS 2024 miscellaneous SimVars](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simvars/miscellaneous-variables/): GROUND ALTITUDE is in metres and is always based on user-aircraft position, regardless of camera or selected aircraft. ARTIFICIAL GROUND ELEVATION supplies a default for AI when scenery is absent; it does not measure terrain. Requesting GROUND ALTITUDE on a distant AI object is therefore not a supported coordinate-query workaround.
2. [MSFS 2024 SimConnect RequestSystemState](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/general/simconnect_requestsystemstate/): used for the live read-only connection check.
3. [MSFS 2024 INITPOSITION](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/structures-and-enumerations/simconnect_data_initposition/): OnGround can position an object on the surface, but this structure only sets data. It cannot be requested as an elevation measurement. Repositioning the user aircraft triggers scenery loading.
4. [Prepar3D world functions](https://ftp.prepar3d.com/SDKv5/sdk/simconnect_api/references/world_functions.html): RequestGroundInfo appears in Prepar3D's API, not the inspected MSFS header/library. This page was discoverable in search; direct retrieval returned an error, so the decisive evidence is the local MSFS header/export inspection.
5. [MSFS ruler tool](https://docs.flightsimulator.com/msfs2024/retail/devmode/menus/tools_info/the-ruler-tool/): supports interactive terrain raycasting in a free flight. This is not a documented external coordinate-query API.

Header inspection covered SimConnect.h and the installed WASM MSFS headers. This investigation does not prove that every possible terrain workflow is impossible. A future experiment could reposition the user aircraft, wait for streamed terrain, and read local elevation at the centre, ends and edges; it would need to prove settling, accuracy and state restoration. That workaround was not tested or silently substituted for the requested direct-query spike. External DEM data likewise requires separate datum/resolution and simulator-fit validation.

## Reproduce

From the repository root, with MSFS running:

```powershell
$env:DOTNET_CLI_HOME="$PWD\.tools\home"
$env:NUGET_PACKAGES="$PWD\.tools\nuget"
& .tools/dotnet/dotnet.exe build spikes/TerrainProbe/TerrainProbe.csproj -c Release
& .tools/dotnet/dotnet.exe spikes/TerrainProbe/bin/Release/net8.0/TerrainProbe.dll 'C:\Users\mattr\Desktop\MSFS 2024 SDK' evidence/terrain-spike/new-run.json
```

The probe refuses an existing report, retries a local connection for up to 60 seconds, waits up to 20 seconds for responses, and closes its own connection. It intentionally returns exit **1** because arbitrary-coordinate elevation is unproven even if the live connection succeeds. It never supplies an elevation to the generator. The retained historical probe-01 predates the optional local SimVar read; probe-02 corresponds to the final functional code.

## Deferred by the elevation gate

No Namibia site or identifier has been selected, no claim of airport separation or terrain flatness is made, and no gravel/dirt material or new scenery package was produced. These require the elevation gate to pass first. WPF remains deferred. FPT1 remains separately installed and its existing visual-test instructions still apply; there is no Skeleton Coast entry to search for on the world map yet.
