# Aircraft condition recording UI

## Implemented

Tracking's existing right-hand diagnostics area contains an expanded Aircraft condition recording section: Start Recording, Stop Recording, status, actual title/model, elapsed duration, frame observation count, selectable JSONL path and Open recording folder. Start requires live identified Tracking telemetry. Manual is the default policy; isolated dry-run mode disables native recording.

`AircraftConditionSession` owns one optional session, cancellation, file naming, progress and connection transitions. It reuses `AircraftConditionRecorder` and `LiveAircraftCondition`; no parallel replacement recorder or new permanent subscription was introduced. The existing V0 diagnostic SimConnect connection exists only while recording. Tracking's existing event provides connection/identity gating; jobs, flight qualification and loading continue receiving the same original observations. Native condition queries remain isolated from those services.

Repeated Start is refused while starting, recording or flushing. Stop is idempotent. Disconnection or an object/title/model change cancels the session, preserving its file; the next aircraft needs a fresh Start and fresh file. An independent guard checks the first condition frame against the identity selected at Start, closing the connection-to-first-frame race. The original recorder also refuses subsequent identity changes. Graceful window close waits for cancellation and file closure; career reset waits before launching its replacement process. Forced process termination/power loss cannot guarantee a footer; already flushed lines remain diagnostic evidence only.

Exports use the unchanged schema-1 session/frame/end JSONL format, with null/unavailable readings preserved. Observations counts frames (each contains multiple field readings). Files are create-new under `<career storage root>/diagnostics/aircraft-condition`. They contain no inferred wear, ownership association or condition mutations. Read/file errors surface in status, preserve any partial file and release the session. There is no automatic restart or automatic deletion/retention policy; users can remove unwanted diagnostic exports through Explorer.

## Future automatic monitoring boundary

Policy currently exposes Disabled and Manual only; there is intentionally no selectable but nonfunctional Automatic mode. A later connection policy can use the same Start/Stop lifecycle after an explicit persistent airframe binding is resolved. Object/title/model identify the current diagnostic session, not a unique owned aircraft. Same-title/model reloads that retain the object ID and never lose tracking cannot yet be distinguished: live reload testing is required.

The reusable `IAircraftConditionSource` frame stream separates observation from the JSONL writer. Future condition processing should consume this stream with an optional raw diagnostic sink, not continuously export large files as its persistent state. Introduce that consumer when automatic monitoring is implemented; avoid a second native reader. Persistent estimates/history remain distinct from diagnostic receipts. This milestone does not update persistent condition, schedule failures, invoke simulator writes, or classify any add-on as compatible.

## MSFS acceptance procedure (pending)

1. Launch the normal Frontier Pilot shortcut and load any aircraft in MSFS 2024. Open Tracking and connect if it is not already live.
2. In Aircraft condition recording, verify the title/model; Start Recording should enable only with identified live telemetry.
3. Click Start. Check RECORDING, an increasing observation count/duration and a JSONL path. Unsupported variables should remain unavailable without stopping the recording.
4. Operate the aircraft normally. Check tracking and normal flight monitoring continue. Do not inject failures for this UI test.
5. Click Stop. Wait for the preserved-file status; open the folder. Inspect session, frame and end lines, read-only/unverified labels and null missing values.
6. Repeat Start/Stop, disconnect while recording, change aircraft while recording, and close the app during recording. Each recording must close; aircraft changes require a fresh Start/file. Reconnect must not start automatically.
7. Repeat separately for default C172, the exact installed Carenado Dakota package and the user's Baron. Record simulator/package versions and keep per-aircraft files. Starting successfully is not compatibility proof; use the capability validation procedure in aircraft-condition-v0.md.

## Automated verification

2026-10-20: 30 focused aircraft-condition cases and all 725 xUnit cases pass in Release. These use synthetic sources/packets and temporary files. New cases cover connected/disconnected start, duplicate actions, stop/closure/restart, disconnection, aircraft change, first-frame mismatch, unavailable values, source errors, output creation errors, shutdown and disabled mode. Production WPF panel bindings are exercised by LoadingWpfSmoke alongside existing payload/fuel controls. Release solution build succeeds. Live MSFS, native cancellation latency and aircraft-specific compatibility remain unverified in this milestone.

Update 2026-10-27: physical-airframe automatic observation now runs independently of this optional manual JSONL export. See physical-aircraft-v1.md. Manual exports still do not modify condition or count exposure.
