namespace FrontierAirportBuilder.Core;

public enum PlatformQualificationStage { Discovered, VisuallyConfirmed, StaticDeckContact, DepartureAndReturn, MissionDestination }
public enum PlatformOperation { Observation, InspectionVisit, CargoDelivery, PersonnelDelivery, PersonnelPickup }
public enum PlatformHandlingCapability { Unknown, DiagnosticOnly, Qualified }
public sealed record PlatformEvidence(string Reference, string SimulatorVersion, string SceneryVersion,
    string AircraftTitle, string AircraftModel, PlatformQualificationStage Stage, PlatformOperation[] Operations)
{
    public bool StaticDeckContact { get; init; }
    public bool GenuineDepartureAndReturn { get; init; }
    public PlatformHandlingCapability CargoHandling { get; init; }
    public PlatformHandlingCapability PassengerHandling { get; init; }
}
public sealed record OffshorePlatform(string Id, string Name, double Latitude, double Longitude, double DeckMeters,
    double ContactRadiusMeters, PlatformQualificationStage Stage, PlatformEvidence[] Evidence)
{
    public string Status => Stage switch
    {
        PlatformQualificationStage.StaticDeckContact => "Static deck contact tested; flown operations not qualified",
        PlatformQualificationStage.MissionDestination => "Qualified for the individually recorded operations and versions",
        _ => Stage.ToString()
    };
    public bool CanLocateObservationArea => Stage >= PlatformQualificationStage.VisuallyConfirmed;
    public bool CanOperate(PlatformOperation operation, string title, string model, string simulator, string scenery) =>
        Stage == PlatformQualificationStage.MissionDestination && !string.IsNullOrWhiteSpace(scenery) &&
        Evidence.Any(e => e.Stage == PlatformQualificationStage.MissionDestination && e.Operations.Contains(operation) &&
            e.StaticDeckContact && e.GenuineDepartureAndReturn &&
            (operation != PlatformOperation.CargoDelivery || e.CargoHandling == PlatformHandlingCapability.Qualified) &&
            (operation is not (PlatformOperation.PersonnelDelivery or PlatformOperation.PersonnelPickup) || e.PassengerHandling == PlatformHandlingCapability.Qualified) &&
            e.AircraftTitle == title && e.AircraftModel == model && e.SimulatorVersion == simulator && e.SceneryVersion == scenery);
    public string BlockedReason => "H125 flown departure/arrival/return, exact scenery version and operation-specific loading/completion acceptance are pending. Static starts and diagnostic ballast do not enable destinations.";
    public string EvidenceSummary => string.Join("\n", Evidence.Select(e => $"{e.AircraftTitle} / {e.AircraftModel} · MSFS {e.SimulatorVersion} · {e.SceneryVersion}\n{e.Reference}\nCargo: {e.CargoHandling}; passengers: {e.PassengerHandling}; genuine departure/return: {(e.GenuineDepartureAndReturn ? "recorded" : "not qualified")}."));
}

/// <summary>Authored qualification facts, not mutable save flags or automatically promoted diagnostic receipts.</summary>
public static class OffshorePlatformRegistry
{
    public static IReadOnlyList<OffshorePlatform> Platforms { get; } = [new("ol400-h01", "OL400 / Valaris 109 H01",
        -22.93892875313759, 14.50461745262146, 39.89600372314453, 8,
        PlatformQualificationStage.StaticDeckContact,
        [new("PR #9: run54 static contact / run56 fixed 5 kg Cargo 1 diagnostic; not a flown mission",
            "1.9.21.0", "Effective platform scenery version unresolved", "H125 Passengers", "H125",
            PlatformQualificationStage.StaticDeckContact, []) { StaticDeckContact = true, CargoHandling = PlatformHandlingCapability.DiagnosticOnly }])];
    public static OffshorePlatform Find(string id) => Platforms.Single(p => p.Id == id);
}
