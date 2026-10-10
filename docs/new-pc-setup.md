# Continue Frontier Pilot on a new PC

## Source and development

Clone the private [MoneyWithMatt/frontier-pilot](https://github.com/MoneyWithMatt/frontier-pilot) repository into a writable folder on Windows x64 while signed into the owning GitHub account:

```powershell
git clone https://github.com/MoneyWithMatt/frontier-pilot.git
cd frontier-pilot
```

Alternatively, clone the full-history `frontier-pilot.bundle` in the local transfer backup:

```powershell
git clone 'D:\FrontierPilot-Transfer\frontier-pilot.bundle' frontier-pilot
cd frontier-pilot
```

Install Git and .NET SDK 8.0.424 (the version selected by `global.json`). The old PC's `.tools` SDK/NuGet cache is intentionally excluded from source control. A standard installed SDK can build the same solution:

```powershell
dotnet restore FrontierAirportBuilder.sln
dotnet build FrontierAirportBuilder.sln -c Release
dotnet test FrontierAirportBuilder.sln -c Release
dotnet publish src/Desktop/FrontierAirportBuilder.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishProfile=DevelopmentWinX64 -o dist/FrontierPilot
& scripts/Create-DevelopmentShortcut.ps1
```

Close Frontier Pilot before republishing. The existing `Publish-Development.ps1` expects a repository-local SDK at `.tools/dotnet/dotnet.exe`; use the standard `dotnet publish` command above on a fresh machine, or provision that local SDK layout before using the script. Do not commit tool caches or generated binaries.

For Codex, open this cloned repository as the project. Start with `AGENTS.md`, `docs/TECHNICAL_STATUS.md`, `docs/DESIGN_DECISIONS.md` and `docs/BACKLOG.md`. The latest completed feature is Career Onboarding V1; see `docs/career-onboarding-v1.md`. Existing gameplay is preserved, while new careers intentionally stop at Coming Soon employers.

## Run without development prerequisites

Copy the entire `application` folder from the transfer backup to a stable local folder and launch `FrontierPilot.exe`. It includes the .NET runtime. The backup is a development distribution, not an installer.

## Career and saved sites

GitHub holds source/history/documentation, not live personal saves. The separate `desktop-state.zip` backup contains the current old-PC data rooted at:

`%LOCALAPPDATA%\FrontierAirportBuilder\desktop`

To retain your career and site designs, close Frontier Pilot on both PCs, back up any existing new-PC data, then extract this archive into `%LOCALAPPDATA%\FrontierAirportBuilder\desktop` before launching. The contents should be directly under `desktop` (for example `desktop/economy` and `desktop/airports`), not nested inside another `desktop` folder. Keep this backup private. Do not use Delete Career unless you intend to reset progress.

Machine-specific workspaces, SDK settings and Community paths must be checked on the new PC. Historical ownership/install receipts do not authorize adopting unrelated packages or prove that scenery is installed there. Retain them for audit; rebuild/reinstall saved site definitions through the application's existing validated workflow. Do not blindly copy packages over unrelated Community content. Saved aircraft positions are last-known observations and do not spawn/move the aircraft.

## MSFS prerequisites

The application now provides **Dependency setup…** on both onboarding and gameplay screens. It can download the qualified SDK 1.2.4 Core and Samples installers from Microsoft, verify the installer signature, open Windows Installer and save a verified SDK folder. See [dependency setup](dependency-setup.md). Native installation and live qualification still require manual acceptance.

Install MSFS 2024 and the qualified SDK needed for scenery compilation. Existing evidence targets SDK 1.2.4; newer SDK/simulator combinations are not automatically qualified by copying this project. Set the user-level `MSFS2024_SDK` to the actual SDK root on the new PC, and recheck simulator connection, survey safeguards and install paths. SDKs and Microsoft scenery assets are not bundled in the transfer backup.
