"""Research-document checks only. Reads pinned Git objects; never edits application source."""
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
SHA = "f7df3ffffa620d39d55b36c7cb74d44805e1ab91"
BASE = "https://github.com/MoneyWithMatt/frontier-pilot/blob/" + SHA + "/"
ANCHORS = {
    "S01": ("src/Core/Economy.cs", "public sealed record ResourceType"),
    "S02": ("src/Workflow/EmployedCargoJobs.cs", "private EmployedCargoJobsSnapshot Present"),
    "S03": ("src/Core/Economy.cs", "public static EconomyTransition AcceptJob("),
    "S04": ("src/Workflow/EmployedCargoJobs.cs", "public async Task<EmployedCargoJobsSnapshot> AcceptAsync"),
    "S05": ("src/Workflow/CompanyEmployment.cs", "public static EconomyTransition Configure("),
    "S06": ("docs/real-time-economy.md", "# Real-time economy"),
    "S07": ("src/Workflow/EmployedCargoJobs.cs", "public async Task PrepareReservedJobsAsync"),
    "S08": ("src/Workflow/FlightAttemptTracker.cs", "public async Task SelectJobsAsync"),
    "S09": ("src/Workflow/FlightAttemptTracker.cs", "public async Task ApplyCargoTransfersAsync"),
    "S10": ("src/Core/AircraftLoading.cs", "public sealed record CargoLoadManifest"),
    "S11": ("src/Core/AircraftLoading.cs", "public static AircraftLoadPlan CreateDesiredState"),
    "S12": ("src/Workflow/AircraftLoadingCoordinator.cs", "private static IReadOnlyList<ManifestShipmentCargo> AllocateShipmentLoads"),
    "S13": ("src/Workflow/AircraftLoadingCoordinator.cs", "public async Task ApplyCargoAsync("),
    "S14": ("src/Workflow/AircraftLoadingCoordinator.cs", "public async Task ApplyAuthorizedCargoAfterBoardingAsync"),
    "S15": ("src/Workflow/AircraftLoadingCoordinator.cs", "private string Access("),
    "S16": ("src/MsfsSdk/SimConnectAircraftLoading.cs", "public Task<AircraftLoadingSnapshot> WritePayloadAsync"),
    "S17": ("src/Core/AircraftLoading.cs", "public static class LoadingUnits"),
    "S18": ("src/Workflow/CompanyEmployment.cs", "public static class CompanyAircraft"),
    "S19": ("src/Workflow/AircraftFleetViewModel.cs", "private string? CheckDispatchCore"),
    "S20": ("src/Core/PassengerTransport.cs", "public static void CheckLimits("),
    "S21": ("src/Workflow/PassengerPayloadCoordinator.cs", "public sealed class PassengerPayloadCoordinator"),
    "S22": ("src/Core/Economy.cs", "public static EconomyTransition ApplyCargoTransfer("),
    "S23": ("src/Core/Economy.cs", "public static EconomyTransition ApplyCargoTransfers("),
    "S24": ("src/Workflow/AircraftLoadingCoordinator.cs", "public async Task UnloadAsync("),
    "S25": ("src/Core/FlightAttempt.cs", "public sealed record FlightAttempt("),
    "S26": ("src/Core/PilotWages.cs", "internal static class PilotWagePayments"),
    "S27": ("src/Core/Economy.cs", "public static EconomyTransition AbandonIncompletePrototypeShipment("),
    "S28": ("src/Workflow/EconomyPersistence.cs", "public sealed class LocalEconomyStateStore"),
    "S29": ("src/Workflow/AircraftLoadingPersistence.cs", "public sealed class LocalAircraftLoadingStore"),
    "S30": ("src/Workflow/FlightAttemptPersistence.cs", "public sealed class LocalFlightAttemptStore"),
    "S31": ("src/Workflow/FlightAttemptTracker.cs", "private FlightAttempt? SelectAttempt("),
    "S32": ("src/Core/PassengerTransport.cs", "public sealed record PassengerTransport("),
    "S33": ("src/Workflow/PassengerTransfersService.cs", "public sealed class PassengerTransfersService"),
    "S34": ("src/Workflow/BuilderViewModel.cs", "var passengerService = new PassengerTransfersService"),
    "S35": ("src/Workflow/EconomyPersistence.cs", "private static void RequireNoPassengerLoad("),
    "S36": ("src/Workflow/AircraftLoadingViewModel.cs", "public string WeightAndBalance"),
    "T01": ("tests/Tests/EmployedCargoJobsTests.cs", "public void AcceptanceReservesEachJobExactlyOnceAndAllowsMultipleReservedShipments"),
    "T02": ("tests/Tests/EmployedCargoJobsTests.cs", "public async Task ServiceReservesMultipleJobsAndRestartDoesNotDuplicateThem"),
    "T03": ("tests/Tests/FlightAttemptTests.cs", "public async Task TwoReservedJobsOnTheSameRouteShareOneFlightAndSettleIndependently"),
    "T04": ("tests/Tests/FlightAttemptTests.cs", "public async Task DifferentRouteCanStayReservedButCannotBeMixedIntoThePreparedLoad"),
    "T05": ("tests/Tests/AircraftLoadingTests.cs", "public void Desired_state_plan_supports_partial_loads_unloads_and_preserves_non_frontier_payload"),
    "T06": ("tests/Tests/AircraftLoadingTests.cs", "public async Task Summary_separates_job_available_loaded_and_requested_cargo"),
    "T07": ("tests/Tests/AircraftLoadingTests.cs", "public async Task Simulator_applied_intent_recovers_without_writing_or_loading_twice"),
    "T08": ("tests/Tests/AircraftLoadingTests.cs", "public async Task Delayed_verification_refuses_a_payload_write_that_the_aircraft_resets"),
    "T09": ("tests/Tests/AircraftLoadingTests.cs", "public void Unit_conversions_are_explicit_and_round_trip"),
    "T10": ("tests/Tests/AircraftLoadingTests.cs", "public void Maximum_gross_and_CG_limits_warn_without_blocking"),
    "T11": ("tests/Tests/AircraftLoadingTests.cs", "public void Desired_state_enforces_authoritative_station_payload_and_gross_limits"),
    "T12": ("tests/Tests/AircraftLoadingTests.cs", "public void Economy_tracks_partial_loading_delivery_and_exactly_once_payment"),
    "T13": ("tests/Tests/PilotWageTests.cs", "public async Task GroupedPhysicalDeliveriesShareOneFlightWageAndRetryIsIdempotent"),
    "T14": ("tests/Tests/AircraftLoadingTests.cs", "public void Wrong_location_unload_requires_confirmation_and_forfeits_without_payment"),
    "T15": ("tests/Tests/AircraftLoadingTests.cs", "public async Task Return_job_retries_persisted_targets_after_restart_without_subtracting_twice"),
    "T16": ("tests/Tests/AircraftLoadingTests.cs", "public async Task Cargo_and_fuel_round_trip_across_store_instances"),
    "T17": ("tests/Tests/FlightAttemptTests.cs", "public async Task Partial_changes_keep_qualification_but_full_origin_return_releases_aircraft_and_requalifies"),
    "T18": ("tests/Tests/PilotWageTests.cs", "public async Task GroundInvalidDisconnectedGapWrongAircraftAndRestartTimeAreNotPaid"),
    "T19": ("tests/Tests/PassengerTransferTests.cs", "public async Task MissionaryVisitUsesSharedTwoPersonPayloadAndExistingFourHundredHourlyWage"),
    "T20": ("tests/Tests/PassengerTransferTests.cs", "public async Task PassengerAndCargoFlightsCannotBoardTogetherAndNoCargoMutationOccurs"),
    "T21": ("tests/Tests/PassengerPayloadTests.cs", "public async Task PartialBoardingIsDurableAndRestartAppliesOnlyRemainingAbsoluteTargets"),
}


def main():
    refs, rows = [], []
    for key, (path, needle) in ANCHORS.items():
        source = subprocess.check_output(["git", "show", f"{SHA}:{path}"], cwd=ROOT).decode("utf-8-sig")
        matches = [i + 1 for i, line in enumerate(source.splitlines()) if needle in line]
        if len(matches) != 1:
            raise ValueError(f"{key}: expected one {needle!r} in {path}, found {matches}")
        url = BASE + path + f"#L{matches[0]}"
        refs.append(f"[{key}]: {url}")
        rows.append(f"| {key} | [{path}:{matches[0]}]({url}) | `{needle}` |")
    report_path = HERE / "report.md"
    report = report_path.read_text(encoding="utf-8")
    used = set(re.findall(r"\]\[([ST]\d+)\]", report))
    assert used == set(ANCHORS), ("unused or missing source refs", used ^ set(ANCHORS))
    generated = "\n".join(refs) + "\n"
    marker = "<!-- generated source refs -->"
    if "--write" in sys.argv:
        report_path.write_text(report.split(marker)[0] + marker + "\n\n" + generated, encoding="utf-8")
        (HERE / "sources.md").write_text(
            "# Pinned source and test index\n\n"
            f"Inspected development commit: `{SHA}`. All links intentionally target that unmerged PR #3 snapshot, not mutable branch lines.\n\n"
            "| Ref | Source | Symbol / test |\n|---|---|---|\n" + "\n".join(rows) + "\n", encoding="utf-8")
    else:
        assert report.split(marker)[1].strip() == generated.strip(), "Stale generated references"
    print(f"PASS: {len(ANCHORS)} unique pinned source/test anchors and all report references ({SHA})")


if __name__ == "__main__":
    main()
