# Development distribution

For the separate hosted CI bundle, provenance/checksums and seven-day download instructions, see [TESTING.md](../TESTING.md). CI publishes to a fresh `.ci-output/development/application` folder, never replaces this local distribution or creates shortcuts. Downloading a CI artifact does not grant approval to execute it on the gaming PC.

Frontier Pilot publishes as a Windows x64, self-contained, single-file WPF application. This is the smallest practical test distribution: it runs without a machine-wide .NET installation and leaves the normal source/build outputs available for Codex development.

The stable executable is:

`dist\FrontierPilot\FrontierPilot.exe`

Run `scripts\Publish-Development.ps1 -CreateDesktopShortcut` after code changes to atomically replace the local distribution and create or refresh the current user's `Frontier Pilot` Desktop shortcut. The shortcut targets the stable executable, so it does not need to be recreated after every publish. `scripts\Create-DevelopmentShortcut.ps1 -Location StartMenu` can create the same user-level shortcut in the Start Menu instead.

The application still needs Windows x64, Microsoft Flight Simulator 2024 for live capture/use, the qualified MSFS 2024 SDK 1.2.4 and the existing user-level `MSFS2024_SDK` setting. Package compilation uses the SDK's external Package Tool; self-contained refers to the .NET runtime, not the Microsoft simulator toolchain.

This is not a customer installer. It has no code-signing certificate, automatic updates, bundled SDK/toolchain, Start Menu uninstaller, installation repair, prerequisite bootstrapper or release channel. Windows may therefore show an unrecognized-publisher warning. Airport data remains in `%LOCALAPPDATA%\FrontierAirportBuilder\desktop` so republishing the executable does not remove saved airports.
