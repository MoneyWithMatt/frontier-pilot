# Verification — 27 October 2026

- Complete xUnit suite: 744 passed (19 new physical-aircraft tests).
- Focused coverage: independent identical airframes/registration and location; corrupt/future/revision protection; stationary/water/elevated/loading contexts; pause/gaps/duplicates; landing bounces; deterministic risk across sample frequency and save/reload; inspection versus repair/replacement; healthy-baseline guards; one-shot fault synchronization; automatic observation/shutdown; pause-event decoding; restart-safe maintenance settlement and company payer; facility/funds refusal; safe placement and unchanged flight counters; interrupted sessions; crash idempotency; disabled effects; same-model object switch; fresh hard-landing episodes.
- Release solution build: passed, zero warnings/errors.
- LoadingWpfSmoke: production payload/fuel and manual recorder bindings passed.
- SiteUiSmoke: existing site/onboarding/employer checks and new physical fleet rendering/unknown location checks passed. Synthetic records are written only under isolated test directories. Rendered screenshots reviewed; unknown locations have no invented map marker and maintenance booking is disabled without qualification.
- No MSFS process or published Frontier Pilot process was running at the process check. No Package Tool, simulator failure command, placement, Community mutation or real maintenance purchase was performed.
- Native time/pause delivery, exact aircraft compatibility, native cancellation latency and concurrent manual/automatic diagnostic load remain live acceptance items.
- Maintenance and command/placement tests use fake adapters and temporary stores, not actual simulator effects.
