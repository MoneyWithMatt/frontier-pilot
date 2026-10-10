# Dependency setup

Open **Dependency setup…** at the top of Frontier Pilot, including before a career has been created.

1. **Detect and recheck** looks for SDK installations in the selected folder, environment configuration, Windows installer registry and common SDK locations. Community2024 is read from the same MSFS UserCfg.opt used by the live builder. Detection never modifies scenery.
2. If missing, download **Core** (264 MB) and **Samples** (3.2 GB). Downloads use Microsoft's pinned SDK 1.2.4 HTTPS URLs; the current survey implementation cannot accept a newer SDK automatically.
3. After each download, choose **Install downloaded component…**. Microsoft Windows Installer displays its normal installation/license/elevation prompts. Install both components into the same SDK folder. Close MSFS and Package Tool before installation. Cancellation is available during downloading/extraction/signature verification; once Windows Installer is open, use its own Cancel action.
4. Recheck, or Browse to a custom SDK location. **Use verified SDK** saves the user-level MSFS2024_SDK setting and updates the current application. SDK settings and dependency downloads remain separate from career data.
5. Load a flight and use the existing connection checks. Successful dependency setup does not establish simulator compatibility or survey qualification.

The downloader streams into unique local folders, checks available space and download length, bounds archive sizes, and extracts only the MSI and its sibling CAB payloads to fixed filenames. ZIP paths cannot choose extraction destinations. Redirects and arbitrary download URLs are refused. The MSI must have a valid Microsoft Corporation Authenticode signature both after downloading and immediately before launch. Missing/cancelled/failed downloads cannot enable installation. Windows installer exit codes are reported; SDK discovery and exact component hashes, not an installer exit alone, determine readiness.

Downloads remain under `%LOCALAPPDATA%/FrontierAirportBuilder/dependency-downloads`; this prototype has no automatic cleanup or download resume. There is no silent installation, Microsoft asset redistribution, automatic SDK upgrade or simulator launch. .NET is included in the self-contained app; MSFS must already be installed by the customer. Community discovery currently uses the existing Steam-style configuration location; Store configuration discovery/manual Community override remains future work.

Official references: [Microsoft SDK overview](https://docs.flightsimulator.com/msfs2024/html/1_Introduction/SDK_Overview.htm), [Microsoft SDK manifest](https://sdk.flightsimulator.com/msfs2024/files/sdk.json). Core/Samples 1.2.4 URLs were verified on the new PC. Microsoft's latest manifest versions are not automatically qualified by this application.

Verification: Release build with zero warnings/errors; 626 solution tests; WPF smoke for setup availability before career activation, displayed detection and download actions. A real Core download/extraction and Microsoft signature check were performed; Samples URL and archive layout were inspected without downloading the entire 3.2 GB. Native installation, post-install live connection and airport compilation remain manual acceptance steps. Personal saves and real Community content were not changed by these tests.
