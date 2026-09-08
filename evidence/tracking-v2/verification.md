# Tracking V2 verification

Date: 2026-09-08

- Installed SDK inspected read-only: 1.2.4 at `C:\Users\mattr\Desktop\MSFS 2024 SDK`.
- Header authority: `SimConnect SDK\include\SimConnect.h` lines 38–42 defines explicit aircraft, avatar and current-user object-ID proxies.
- Official sample authority: SimvarWatcher exposes `USER_AVATAR` and `USER_CURRENT` object types.
- Existing empirical authority reused unchanged: `evidence/avatar-spike/sedona-01` and `docs/avatar-spike.md`.
- Full Release test suite: 334 passed, 0 failed.
- Full Release solution build: 0 warnings, 0 errors.
- Windows x64 self-contained publish completed to an isolated validation directory; a dry-run UI smoke remained healthy until the test process was closed.
- The stable `dist\FrontierPilot` replacement was deliberately deferred because that published executable was already running; it was not terminated in case it held unsaved user work.
- Live V2 Tracking map integration awaits Matt's manual seated/walk/return acceptance. No new simulator session was required to implement or test the read-only boundaries.
