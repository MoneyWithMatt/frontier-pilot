# Tracking V2 verification

Date: 2026-09-08

- Installed SDK inspected read-only: 1.2.4 at `C:\Users\mattr\Desktop\MSFS 2024 SDK`.
- Header authority: `SimConnect SDK\include\SimConnect.h` lines 38–42 defines explicit aircraft, avatar and current-user object-ID proxies.
- Official sample authority: SimvarWatcher exposes `USER_AVATAR` and `USER_CURRENT` object types.
- Existing empirical authority reused unchanged: `evidence/avatar-spike/sedona-01` and `docs/avatar-spike.md`.
- Full Release test suite: 334 passed, 0 failed.
- Full Release solution build: 0 warnings, 0 errors.
- Windows x64 self-contained publish completed to an isolated validation directory; a dry-run UI smoke remained healthy until the test process was closed.
- After Matt closed the prior application, `scripts\Publish-Development.ps1 -CreateDesktopShortcut` atomically replaced the stable self-contained build at `dist\FrontierPilot\FrontierPilot.exe` and refreshed `Desktop\Frontier Pilot.lnk`. SHA256: `B8DA2D73E2A703FE86A848F4118E95999430568A5FD5A7F4C1736931BF788C3A`. The published dry-run UI remained running during its smoke interval and only that test process was then closed.
- Live V2 Tracking map integration awaits Matt's manual seated/walk/return acceptance. No new simulator session was required to implement or test the read-only boundaries.
