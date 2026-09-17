# EFB payload synchronisation investigation — 2026-09-17

Read-only investigation of Frontier source, installed SDK 1.2.4 EFB declarations and SimConnect header, installed Black Square Turbine Duke package 0.1.2, and primary developer sources. No simulator writes, installed-package edits, economy changes or live comparison were performed.

## Confirmed

- Frontier `src/MsfsSdk/SimConnectAircraftLoading.cs` uses `SimConnect_SetDataOnSimObject` with `PAYLOAD STATION WEIGHT:n`, FLOAT64 pounds, sorted one-based indices, and explicit kg/lb conversion. Readback uses the same variables. It does not notify either EFB. Requests/writes target object ID 0; object targeting during walkaround remains a separate consideration from seated EFB refresh.
- The stock MSFS 2024 EFB failing to refresh after a payload SimVar write is explicitly acknowledged by MSFS developer support. Topic 17939 links the issue to tracked ticket 14914. In the latter, support reported it was still work in progress on 2026-07-08. This supports a simulator-side synchronisation defect; screenshots alone did not prove that every EFB value was simply a saved plan.
- Installed Duke `BlackSquareTablet.js` maps Nose Baggage to station 1 and Rear Baggage to station 8. `updatePayloadBlock` reads `PAYLOAD STATION WEIGHT:n`; `updatePayloadTextBlock` reads `TOTAL WEIGHT`. `updateSimVars` refreshes the payload page at a nominal 2500 ms interval, and selecting the Payload tab refreshes it too. Changing display units also refreshes the blocks. These observations do not establish which runtime context/cached values were used in Matt's screenshot.
- Duke model behaviour has separate save/restore payload logic. This proves another writer exists, but does not establish that it caused this particular discrepancy.
- In Matt's screenshot, Frontier gross 5673.8 lb agrees with Duke tablet gross 5674 lb. Their distributions disagree: Frontier forward baggage 0, rear baggage about 260.5 lb; tablet nose 110, rear 150 lb. Approximately matching aggregate weights do NOT establish correct station distribution or CG.
- Stock EFB screenshots show seats 340 lb, cargo 30 lb and total 5643 lb. These are not a simultaneous controlled baseline; the approximately 31 lb total difference alone cannot establish the cause.

## Sources

- [MSFS 2024 EFB does not update after SimVar changes](https://devsupport.flightsimulator.com/t/weights-displayed-in-efb-do-not-update-when-payload-is-changed-by-accessing-simvar-via-code/17939)
- [Tracked issue and July 2026 support update](https://devsupport.flightsimulator.com/t/payload-station-weight-does-not-have-an-impact-on-copilot-display/14914)
- [Independent aircraft developer reproduction](https://devsupport.flightsimulator.com/t/efb-cargo-payload-station-weight-issue/17110)
- [Older MSFS payload-menu issue](https://devsupport.flightsimulator.com/t/payload-menu-does-not-update/6063): partly fixed in MSFS 2020 SU14; do not treat it as proof of a 2024 fix.
- [Black Square Duke manual](https://downloads.justflight.com/products/JFF003869/BKSQ_PistonDukeManual.pdf): documents native payload interface desynchronisation. This is corroboration, not a guarantee that its suggested UI resynchronisation preserves a Frontier manifest.
- [OnAir tracking documentation](https://onair.company/your-first-flight/4-track/): supports Autoset and manual aircraft-interface loading; certain aircraft disable Autoset.
- [OnAir compatibility update](https://www.onair.company/06-08-2022-step-2-of-user-interface-enhancements/): explicitly associates fuel/payload Autoset compatibility with official SimConnect. Does not disclose exact station writes or an EFB refresh API.

Web pages that failed browser extraction were inspected using the public Discourse topic JSON endpoints. No source code from third-party packages is copied into this repository.

## Fix feasibility and smallest next test

No supported generic EFB payload-refresh API was found in the inspected SDK header/EFB declarations. This is a bounded negative finding, not proof that no workaround exists. Do not implement repeated payload writes, aircraft reloads or undocumented EFB mutations merely to refresh a display; these could overwrite user loading or interfere with recovery.

For the Duke, first switch its tablet from Payload to Options and back, wait at least five seconds, then compare a fresh Frontier read with both baggage stations and gross weight. A display-unit toggle is another read-refresh trigger in the inspected tablet code. Neither requires editing payload. If mismatch persists, capture simultaneous seated SimConnect and in-cockpit SimVar values for stations 1–8, total weight, fuel weight, empty weight and CG; resolve object identity/context and any add-on overwrite before changing the adapter.

For OnAir, establish whether the reported match used MSFS 2024, this Duke, Autoset, and the stock EFB or Duke tablet. A controlled comparison should run one payload writer at a time with no active Frontier cargo affected. Record station-level values before and after, not just gross totals. Different timing/distribution/loading direction is a hypothesis until tested.

Potential outcomes: use an officially supported refresh if found; provide an aircraft-specific supported loading adapter if required; or offer explicit manual aircraft-EFB loading followed by Frontier verification. That last option would require its own transaction design and is not implemented by this investigation. Do not modify the installed Duke or stock EFB files.
