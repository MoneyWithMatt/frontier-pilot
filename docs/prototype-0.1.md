# Frontier Airport Builder Prototype 0.1 — generated FPT1

## Delivered and verified

The CLI generated and compiled **Frontier Test Strip (FPT1)** using a minimal Frontier-maintained template and MSFS 2024 SDK 1.2.4. The real Package Tool returned 0, no new file-based builder errors appeared, and all 11 package layout entries were checked for existence and byte size. The package includes a 514-byte airport BGL, manifest, layout and the proven material dependencies.

The result is preserved in `artifacts/fpt1/frontier-airport-fpt1.zip`. Build commands, input/output hashes, generated XML, geometry, manifest, layout, stdout/stderr and validation result are under `evidence/prototype-0.1/fpt1`. The original isolated build is `workspaces/fpt1-generated-01`. This proves generation and package compilation. The package has not been installed in Community or visually inspected in a flight; the deliberately reused sample site overlaps the real MYNN area and is only a build test location.

No WPF UI, terrain-elevation acquisition, settlements, economy or multiplayer were implemented.

## Configuration correction

The user-level `MSFS2024_SDK` override is now `C:\Users\mattr\Desktop\MSFS 2024 SDK\`, verified against version.txt, Package Tool and ModelBehaviorDefs. At the time of this change the user value was unset and the machine value was the incorrect Desktop path. The machine value was left unchanged. Exact before/after state and time are in `evidence/prototype-0.1/environment-change.json`.

Existing processes may retain the older environment. The CLI resolves the explicit SDK argument first, then the user override, then its process environment, and always sets the child's SDK root explicitly. To undo this user override, restore the `Before` value from the evidence (null means delete the user variable), without changing the machine setting.

## Core contract

`AirstripDefinition` contains a name, four-character uppercase identifier, runway-centre latitude/longitude, fixed altitude in metres, true heading, east-positive magnetic variation, length/width in metres and surface. All fields must be supplied in CLI JSON. Unknown JSON fields and unsupported surface values are rejected. Prototype 0.1 exposes only the baseline-proven Asphalt material.

Validation collects errors before generation. Names are 1–48 printable BMP characters without surrounding whitespace; XML-sensitive characters are safely escaped. Identifiers are `[A-Z][A-Z0-9]{3}`. Coordinates must be finite; latitude is strictly between the poles and longitude is [-180,180]. Prototype limits: altitude [-500,9000] m, length [10,10000] m, width [1,500] m and width less than length, magnetic variation [-180,180]. These limits are product safeguards for this spike, not claimed SDK limits.

Finite true headings normalize to [0,360). Runway numbers use normalized magnetic heading (true minus east-positive variation), rounded to the nearest ten degrees, with exact five-degree ties upward and north mapped to 36. Reciprocal numbers differ by 18. XML uses west-positive magvar, so it receives the negated model value.

`Geodesy.Direct` implements the WGS84 Vincenty direct solution. Runway endpoints lie half the length from the centre, in opposite directions; starts face inward using the arrival bearings at their endpoints. This accounts for meridian convergence rather than reusing the centre heading at both ends. The example is runway pair 10/28 with a flat altitude of 4.1148001316736 m. See template provenance for all conversions from the sample.

## XML and template

`ProjectGenerator` loads embedded XML templates, edits them with **System.Xml.Linq**, and writes fixed-order UTF-8 without BOM, LF line endings and invariant numeric formatting. No XML string substitution is used. Output bytes do not depend on culture, output directory or current time. Material GUIDs are deterministically derived per identifier so separate packages do not share the sample GUID. The generated folder must not already exist; existing work is never overwritten.

The template has exactly one Airport, one Runway and two RunwayStart elements. It retains the proven project/package layout and the small content/material dependency groups. `PackageOrderHint=CUSTOM_AIRPORT` is mandatory. Its provenance, changes and retained third-party assets are recorded in `src/MsfsSdk/Templates/PROVENANCE.md`. The checked-in template is the actual successful generated FPT1 source, usable as a standalone project as well as the generator input.

## Build runner

`PackageBuilder` passes the XML path and `-nopause` as separate process arguments, uses the generated project directory, and sets `MSFS2024_SDK` for its child. It creates no Community installation. A named semaphore prevents overlapping Frontier builds, and existing MSFS/Package Tool processes cause a clear refusal because the simulator error log is shared.

Each build uses fresh output, streams stdout/stderr to evidence, captures the simulator error-log delta and records executable hash/version/arguments. Success requires zero exit, no timeout, no new builder errors, CUSTOM_AIRPORT metadata, a valid layout with all listed files/sizes, and a non-empty BGL in the layout. The previous false-positive exit-zero case has regression coverage. Missing, malformed, duplicate and path-escaping layout entries fail validation.

The CLI bounds a build at three minutes. Cancellation/timeout terminates the launcher only, never a whole Steam process tree; a simulator launched by Steam may need to finish separately. These are prototype controls, not a general simulator process manager. Steam must be ready to launch the installed licensed simulator; the interactive game and Developer Mode need not be started beforehand for the proven command-line build.

## Verification

All **152 unit tests pass**. The suite covers validation boundaries/non-finite inputs, heading wrapping and rounding, all 36 reciprocal pairs, independent GeographicLib WGS84 reference cases, endpoints/centreline properties, XML structure/material references/escaping, byte determinism across cultures and paths, refusal to overwrite existing work, and package-validation failures. Reference generation is reproducible without introducing a production geometry dependency.

Eight additional CLI rejection checks pass, including missing required fields, unknown properties, string/numeric unsupported surfaces, invalid identifiers/dimensions, existing output preservation and extra arguments. A final generation from the now-canonical template matched all nine actual compiled input files byte-for-byte. This checks that the final source still reproduces the successful build input after the template was canonicalized.

Final build/test and CLI verification evidence is stored in `evidence/prototype-0.1`. The earlier successful SDK baseline and all nine installed sample files remain hash-identical to their preserved inventories.

## Reproduce

From the repository root with .NET SDK 8.0.424 (or its permitted patch):

```powershell
dotnet test FrontierAirportBuilder.sln -c Release
dotnet run --project src/App -c Release -- generate examples/frontier-test-strip.json workspaces/my-fpt1-source
dotnet run --project src/App -c Release -- build examples/frontier-test-strip.json workspaces/my-fpt1-build 'C:\Users\mattr\Desktop\MSFS 2024 SDK'
```

Use a new directory for each invocation. On this PC the repository-local executable `.tools/dotnet/dotnet.exe` supplies the pinned .NET SDK. Exit codes are 0 for success, 1 for invalid input or generation/build failure, and 2 for usage errors.
