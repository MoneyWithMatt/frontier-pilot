# Real-time economy and SYSTEM missions — 2026-10-03

The world uses the computer's real UTC wall clock, displayed in local time in the app header. Simulator date, pause and accelerated flight time do not change economic time.

SYSTEM offers are renewed on a persisted three-hour cadence and expire six hours after creation if unaccepted. The board holds up to two offers per enabled recurring demand, so additional work can appear while another shipment is reserved. The existing verified Eros/Uis/Arandis catalogue gates and temporary cargo/payout configuration remain in force. No unverified destinations are introduced. Offer expiry is visible in Jobs; the banner shows the next update.

Accepted, loaded or in-transit jobs never expire through this mechanism. Player-created logistics contracts retain their own cancellation and road fallback rules. An expired SYSTEM offer becomes an audited terminal job with a cancelled requirement. It never had a shipment reservation, so expiry changes no cargo inventory and pays nothing. Gateway provisioning continues through the existing audited SYSTEM supply mechanism, accounting for stock promised to live offers.

The same real-time checkpoint advances operation production once per elapsed 24-hour world day using the existing generic process/archetype kernel. It consumes available, unreserved stock and retains supply/storage constraints. Road fallback/arrival events are processed before each elapsed production boundary and at the final catch-up time. The old manual day button is removed from the normal UI; its domain/test method remains for deterministic tests. UI process descriptions now refer to real days.

Economy schema 7 adds nullable RealTime scheduling and offer timestamps. Existing saves activate the clock at their first load after this update: existing offered SYSTEM jobs get a full six-hour window, and no historical production is imposed. Completed/accepted history is preserved. Future/corrupt documents still fail without replacement.

The desktop refreshes world state every 30 seconds, with no timer overlap and no flight-tracker reinitialization. Checkpoints write only when an event is due, not every timer tick. On restart, elapsed real days and roads reconcile once; missed obsolete boards are not generated just to expire them. Catch-up is bounded to 365 production days per transaction and retains the backlog for subsequent refreshes. Clock reversal does not replay persisted events. This is local prototype timekeeping, not trusted multiplayer time.

Configuration: EconomyRealTimeState stores OfferIntervalHours (default 3), OfferLifetimeHours (6) and OfferSlotsPerDemand (2); defaults are centralized in EconomyRealTime. Future balancing can change these without rebuilding the mission lifecycle. Current cadence adds another set at three hours, replaces the original expired set at six hours, and continues every three hours.

Verification covers deadline acceptance refusal, accepted loaded cargo survival, inventory/payment preservation on expiry, elapsed production, offline board catch-up, concurrent restart, failed atomic replacement, road completion and player escrow protection. Tests use injected timestamps and isolated files; no clock-changing or live simulator writes are needed.

Manual check: launch, inspect the header date/time and Jobs next-update/expiry values, reserve a job and fly normally. After a refresh boundary, new work appears; accepted work stays reserved. Close/reopen to check that the schedule and reservations survive.

Validation: 589 complete-solution tests passed, including 8 new real-time/contract tests. Release build reported zero warnings/errors. Isolated WPF navigation/edit smoke passed and the Jobs schedule/expiry display was rendered for review. Windows self-contained app and Desktop shortcut republished. No live MSFS acceptance claimed.
