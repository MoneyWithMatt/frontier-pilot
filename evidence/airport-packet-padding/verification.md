# Airport-list packet padding correction — 2026-10-16

User screenshot showed `Invalid airport list packet sequence` after Check all airports. Read-only inspection of the already-running simulator reproduced the cause:

- First reply: actual and declared size 41,068 bytes; 1,139 airports; packet 0 of 75.
- Packed header + count × entry size = 41,032 bytes. The reply includes one additional 36-byte array slot, consistent with the native structure's one-element placeholder.
- Previously the reader demanded exactly header + count × entry size. It now permits that size or exactly one extra slot, reads only the declared record count, verifies declared buffer length, and retains bounded counts, valid coordinates, unique packet indexes and complete sequence requirements. It still refuses truncation and arbitrary extra bytes.
- Error messages now include packet dimensions to distinguish sequence and layout failures.

## Live result

Corrected production reader queried the already-running MSFS process without changing flight state, launching MSFS, running Package Tool, or writing personal career/cache files.

Observed UTC: **2026-10-16 17:24:06**. SDK **1.2.4**, simulator **1,9,18,0**. Completed worldwide response: **84,319 records**.

Of the 39 distinct supplied identifiers, **33 matched exactly once**, six were absent. No supplied identifier was ambiguous. Nine additional research candidates have no identifier.

Found memberships: Missionary **13**, Industrial **9**, Safari **8**, Offshore **8** (including its own FYWB base, so seven possible destinations). Board dispatch still selects at most four destinations using the existing time bias.

Safari found: **FYNG, FYMO, FYGK, FYAD, FYHV, FYHO, FYOO, FYME**.

Identifiers not returned: **FYSF — Sesfontein; FYBM — B2Gold Otjikoto Mine; FYRP — Rosh Pinah; FYTF — Twyfelfontein; FYDN — Doro Nawas; FYMW — Moewe Bay**. This means absent from this simulator's completed airport-list response, not proof that the real airfields do not exist.

No scenery quality, runway suitability, passenger operations or actual delivery flight was verified. Personal saves were not changed by this diagnostic.

## Automated verification

- Focused airport lookup tests: **12 passed**.
- Complete solution suite: **672 passed**, zero failed/skipped.
- Release solution build: zero warnings/errors.
- Normal self-contained development distribution republished with `scripts/Publish-Development.ps1 -CreateDesktopShortcut` after the user closed the app. Executable SHA256: `0D82676E3EFA337B5FB96E0D3A4D524949ECFC76E02D9F324B9E281028E99891`. Generated binaries remain untracked.
- New regression cases reproduce the observed 41,068-byte packet and verify that trailing slot contents are ignored; missing packets, arbitrary trailing bytes and declared-size mismatches remain rejected.
