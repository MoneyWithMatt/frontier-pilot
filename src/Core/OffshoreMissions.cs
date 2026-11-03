namespace FrontierAirportBuilder.Core;

public enum OffshoreMissionKind { Observation, InspectionVisit, CargoDelivery, PersonnelDelivery, PersonnelPickup }
public enum OffshoreMissionState { Offered, Accepted, Outbound, Observing, Returning, ReadyToComplete, Completed, Expired }
public sealed record OffshoreOperatingArea(string Id, string Name, double Latitude, double Longitude,
    double RadiusNauticalMiles = 5, int ObservationSeconds = 300)
{
    public void Validate()
    {
        EconomyValidation.Id(Id, "operating area"); EconomyValidation.Text(Name, "area name", 160);
        if (!double.IsFinite(Latitude) || Latitude is <= -90 or >= 90 || !double.IsFinite(Longitude) || Longitude is < -180 or > 180 ||
            !double.IsFinite(RadiusNauticalMiles) || RadiusNauticalMiles is < .5 or > 50 || ObservationSeconds is < 10 or > 7200)
            throw new InvalidDataException("Invalid offshore observation area/duration.");
    }
}
public sealed record OffshoreMission(string Id, string EmployerId, string PlayerId, string Title, string Purpose,
    PassengerEndpoint Base, OffshoreOperatingArea Area, DateTimeOffset OfferedUtc, DateTimeOffset ExpiresUtc,
    decimal HourlyRate, OffshoreMissionState State = OffshoreMissionState.Offered, Guid? AirframeId = null,
    FlightAircraftIdentity? Aircraft = null, DateTimeOffset? AcceptedUtc = null, DateTimeOffset? DepartedUtc = null,
    DateTimeOffset? ObservationCompletedUtc = null, DateTimeOffset? ReturnReadyUtc = null, DateTimeOffset? CompletedUtc = null,
    long AirborneTicks = 0, double ObservationSeconds = 0, bool RecoveryRequired = false,
    FlightPosition? LastPosition = null, double? LastSimulationSeconds = null)
{
    public OffshoreMissionKind Kind { get; init; } = OffshoreMissionKind.Observation;
    public bool Active => State is not (OffshoreMissionState.Offered or OffshoreMissionState.Completed or OffshoreMissionState.Expired);
    public string Route => Base.Name + " → " + Area.Name + " → " + Base.Name;
    public string AircraftRequirement => "Cirrus Vision Jet · selected employer airframe and exact MSFS binding";
    public string WageDisplay => DollarDisplay.Format(HourlyRate) + " / verified airborne hour";
    public string ObservationDisplay => $"{Math.Min(ObservationSeconds, Area.ObservationSeconds):0} / {Area.ObservationSeconds} seconds · {Area.RadiusNauticalMiles:0.#} NM area";
    public PilotWage Wage(string employerName) => new(Id, EmployerId, employerName, AirborneTicks, HourlyRate);
    public OffshoreMission EnsureValid()
    {
        EconomyValidation.Id(Id, "offshore mission"); EconomyValidation.Id(EmployerId, "mission employer"); EconomyValidation.Id(PlayerId, "mission pilot");
        EconomyValidation.Text(Title, "mission title", 160); EconomyValidation.Text(Purpose, "mission purpose", 400);
        Base.EnsureValid(); Area.Validate();
        if (!Base.CanGenerate || Kind != OffshoreMissionKind.Observation || !Enum.IsDefined(State) || HourlyRate <= 0 ||
            OfferedUtc == default || ExpiresUtc <= OfferedUtc || AirborneTicks < 0 || !double.IsFinite(ObservationSeconds) ||
            ObservationSeconds < 0 || ObservationSeconds > Area.ObservationSeconds)
            throw new InvalidDataException("Invalid offshore mission policy/progress.");
        if (State is OffshoreMissionState.Offered or OffshoreMissionState.Expired)
        {
            if (AcceptedUtc is not null || AirframeId is not null || Aircraft is not null || DepartedUtc is not null ||
                LastPosition is not null || LastSimulationSeconds is not null || AirborneTicks != 0 || ObservationSeconds != 0 ||
                ObservationCompletedUtc is not null || ReturnReadyUtc is not null || CompletedUtc is not null)
                throw new InvalidDataException("Unaccepted mission contains flight progress.");
            return this;
        }
        if (AcceptedUtc is null || AcceptedUtc < OfferedUtc || AcceptedUtc >= ExpiresUtc || AirframeId is null || AirframeId == Guid.Empty)
            throw new InvalidDataException("Mission acceptance/airframe is invalid.");
        if (State == OffshoreMissionState.Accepted && Aircraft is null && LastPosition is null && DepartedUtc is null &&
            LastSimulationSeconds is null && AirborneTicks == 0 && ObservationSeconds == 0 && ObservationCompletedUtc is null && ReturnReadyUtc is null && CompletedUtc is null)
            return this;
        if (Aircraft is null) throw new InvalidDataException("Mission has no recorded aircraft identity.");
        Aircraft.EnsureValid(); LastPosition?.EnsureValid();
        if ((LastPosition is null) != (LastSimulationSeconds is null) || LastSimulationSeconds is < 0 ||
            LastSimulationSeconds is {} time && !double.IsFinite(time) ||
            LastPosition is {} last && last.ObservedUtc < AcceptedUtc ||
            AirborneTicks > ((LastPosition?.ObservedUtc ?? AcceptedUtc.Value) - AcceptedUtc.Value).Ticks)
            throw new InvalidDataException("Mission flight clock/position is invalid.");
        if (DepartedUtc is {} departed && departed < AcceptedUtc ||
            ObservationCompletedUtc is {} observed && (DepartedUtc is null || observed <= DepartedUtc) ||
            ReturnReadyUtc is {} returned && (ObservationCompletedUtc is null || returned <= ObservationCompletedUtc) ||
            (State is OffshoreMissionState.ReadyToComplete or OffshoreMissionState.Completed) != (ReturnReadyUtc is not null) ||
            (State == OffshoreMissionState.Completed) != (CompletedUtc is not null) ||
            CompletedUtc is {} completed && completed < ReturnReadyUtc ||
            ObservationCompletedUtc is not null && ObservationSeconds != Area.ObservationSeconds)
            throw new InvalidDataException("Offshore mission stages are out of order.");
        return this;
    }
}

public static class OffshoreMissionKernel
{
    public static string OfferId(string employer, long cycleTicks) => EconomyIds.Derive("offshore-", employer + "-" + cycleTicks);
    public static bool Occupied(EconomyWorld world) => world.OffshoreMissions.Any(m => m.Active && m.Aircraft is not null);
    public static void RequireTransportIdle(EconomyWorld w)
    {
        if (CargoJourneyKernel.Occupied(w) || w.Shipments.Any(s => s.LoadedQuantity > 0 || s.State == ShipmentState.Loaded) ||
            w.PassengerTransfers.Any(t => t.State == PassengerTransferState.Boarded) ||
            w.PassengerTransports.Any(t => t.State is PassengerCarriageState.Boarding or PassengerCarriageState.Aboard or PassengerCarriageState.Arrived or PassengerCarriageState.Disembarking))
            throw new InvalidOperationException("Finish the current cargo/passenger flight or loading recovery before starting an observation assignment. Existing loads are preserved.");
    }
    public static EconomyTransition Replace(EconomyWorld w, OffshoreMission m) =>
        new((w with { Revision = w.Revision + 1, OffshoreMissions = w.OffshoreMissions.Select(x => x.Id == m.Id ? m.EnsureValid() : x).ToArray() }).EnsureValid(), true);
    public static EconomyTransition Complete(EconomyWorld w, string id, DateTimeOffset utc)
    {
        var m = w.OffshoreMissions.Single(x => x.Id == id);
        if (m.State == OffshoreMissionState.Completed) return new(w, false);
        if (m.State != OffshoreMissionState.ReadyToComplete || m.ReturnReadyUtc > utc || m.RecoveryRequired)
            throw new InvalidOperationException("Return and stop at base with fresh qualified tracking before completing.");
        var wage = m.Wage(w.Businesses.Single(x => x.Id == m.EmployerId).Name).EnsureValid();
        var player = w.Players.Single(x => x.Id == m.PlayerId);
        var entry = new FinancialTransaction(EconomyIds.Derive("offshore-wage-", m.Id), FinancialTransactionKind.OffshoreMissionWage,
            null, m.Id, m.EmployerId, m.PlayerId, wage.Amount, player.CurrencyCode, utc) { Wage = wage };
        var updated = w with { Revision = w.Revision + 1,
            OffshoreMissions = w.OffshoreMissions.Select(x => x.Id == id ? m with { State = OffshoreMissionState.Completed, CompletedUtc = utc } : x).ToArray(),
            FinancialLedger = w.FinancialLedger.Append(entry).ToArray() };
        updated = updated with { Players = updated.Players.Select(p => p.Id == player.Id
            ? p with { CachedBalance = EconomyLedger.Balance(updated, p.Id, p.CurrencyCode) } : p).ToArray() };
        return new(updated.EnsureValid(), true);
    }
    public static void Validate(EconomyWorld w)
    {
        EconomyValidation.Collection(w.OffshoreMissions, "offshore missions");
        EconomyValidation.Unique(w.OffshoreMissions.Select(m => m.Id), "offshore mission");
        if (w.OffshoreMissions.Count(m => m.Active) > 1) throw new InvalidDataException("Only one observation flight may be active.");
        if (Occupied(w)) RequireTransportIdle(w);
        foreach (var m in w.OffshoreMissions)
        {
            m.EnsureValid();
            if (!w.Players.Any(p => p.Id == m.PlayerId && p.EmployerBusinessId == m.EmployerId) ||
                w.Businesses.SingleOrDefault(b => b.Id == m.EmployerId)?.PilotHourlyRate != m.HourlyRate)
                throw new InvalidDataException("Offshore mission employer/wage differs from the career.");
            var entries = w.FinancialLedger.Where(t => t.Kind == FinancialTransactionKind.OffshoreMissionWage && t.EvidenceId == m.Id).ToArray();
            if (entries.Length != (m.State == OffshoreMissionState.Completed ? 1 : 0)) throw new InvalidDataException("Mission completion and wage receipt disagree.");
        }
    }
    public static void ValidateWage(EconomyWorld w, FinancialTransaction t)
    {
        var m = w.OffshoreMissions.SingleOrDefault(x => x.Id == t.EvidenceId);
        if (m?.State != OffshoreMissionState.Completed || t.JobId is not null || t.SiteId is not null ||
            t.Id != EconomyIds.Derive("offshore-wage-", m.Id) || t.FromEntityId != m.EmployerId || t.ToEntityId != m.PlayerId ||
            t.OccurredUtc != m.CompletedUtc || t.Wage != m.Wage(w.Businesses.Single(b => b.Id == m.EmployerId).Name) ||
            t.Amount != t.Wage.Amount || t.CurrencyCode != w.Players.Single(p => p.Id == m.PlayerId).CurrencyCode)
            throw new InvalidDataException("Invalid offshore mission wage receipt.");
    }
}
