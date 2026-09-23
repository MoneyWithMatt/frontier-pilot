# Persistent airport management

Frontier Pilot stores its own versioned airport documents under `%LOCALAPPDATA%\FrontierAirportBuilder\desktop\airports`. Each JSON document contains the airport identity, guarded survey evidence, runway, parking stands, multi-point taxiways, placed scenery objects, revision and historical build/install status. Generated MSFS files are outputs, never the source of truth.

The My Airports tab lists readable documents. Open restores the captured definition into `Survey Captured`; it deliberately clears current build and installation authority. Confirm the site again, validate and rebuild before installation. Save is explicit, and a successful build also saves the exact definition it built. Identifier changes are blocked after the initial save.

Generated projects live under the managed desktop state directory. Every project has an airport-specific ownership marker and an exact file inventory. Installations continue to use the existing package receipt and hashes. Delete Airport first makes a read-only deletion preview and then asks for confirmation. At commit time it checks that the saved document revision, preview fingerprint, project ownership/inventory and installed package receipt/inventory still match. It moves verified targets into an app-owned recovery area before removing them and rolls back ordinary failures.

Deletion removes only the selected airport's saved JSON, registered generated project directories, and its currently installed Frontier package when ownership is intact. It refuses unknown files, changed packages, redirected paths, mismatched ownership, stale confirmations and running MSFS. Other saved airports, unrelated Community content, SDK files, original survey evidence, build history and previous-install backups are preserved.

## Manual dry-run check

1. Start `scripts/Start-Desktop.ps1 -DryRun`.
2. Check, capture, enter a unique identifier, add parking and a taxiway, confirm the site, validate and build.
3. Select **Save site** if it has not already been saved by build.
4. Close Frontier Pilot, start the same dry-run command again, and open **My Airports**.
5. Select the airport and choose **Open airport**. Confirm its runway, parking and taxiway layout is restored and Build remains disabled until site confirmation and validation.
6. Create a second disposable airport. In My Airports choose **Delete Airport**, inspect the exact preview and cancel once. Repeat and confirm. Verify the other airport remains.

The dry-run library uses a stable sandbox path recorded in `%LOCALAPPDATA%\FrontierAirportBuilder\dry-run-profile.txt`. It never writes the real Community directory. Live deletion must be tested only with a disposable Frontier-owned airport while MSFS is closed.
