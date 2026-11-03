namespace FrontierAirportBuilder.Core;

public enum PlatformVisitStage { AwaitingDeck, ReadyForDeparture, ContactLoss, AwaitingReturn, Completed, Blocked }
public enum PlatformVisitMilestone { DeckEstablished, DepartureObserved, ReturnObserved }
public sealed record PlatformVisitEvent(PlatformVisitMilestone Milestone, DateTimeOffset ObservedUtc, double SimulationSeconds);
public sealed record PlatformVisitStatus(PlatformVisitStage Stage, string Reason, bool CurrentDeckPermission,
    double ContactLossSeconds, IReadOnlyList<PlatformVisitEvent> Events)
{
    public bool Completed => Stage == PlatformVisitStage.Completed;
}

/// <summary>
/// Fixed OL400 diagnostic: deck -> sustained contact loss -> deck again. Not a flown route,
/// transport assignment, physical airframe binding, VerifiedDeliveryEvidence or payment authority.
/// </summary>
public sealed class ExperimentalPlatformVisit
{
    private readonly ExperimentalPlatformPresence presence = new();
    private readonly List<PlatformVisitEvent> events = [];
    private ConditionAircraftIdentity? aircraft;
    private PlatformContactObservation? previous;
    private double? lossStart;
    private bool armed, blocked;
    public PlatformVisitStatus Current { get; private set; } =
        new(PlatformVisitStage.AwaitingDeck, "Establish 20 seconds of current OL400 deck presence.", false, 0, []);

    public PlatformVisitStatus Observe(ConditionFrame? frame, DateTimeOffset now)
    {
        if (blocked) return Current;
        var deck = presence.Observe(frame, now);
        PlatformVisitStatus Status(PlatformVisitStage stage, string reason, double duration = 0) =>
            Current = new(stage, reason, deck.DiagnosticDeckPresence && frame?.Runtime is { Running: true, Paused: false } &&
                frame.Aircraft.Model == "H125" && (aircraft is null || aircraft == frame.Aircraft),
                duration, events.ToArray());
        bool Has(PlatformVisitMilestone milestone) => events.Any(x => x.Milestone == milestone);
        void Record(PlatformVisitMilestone milestone, PlatformContactObservation sample)
        {
            if (!Has(milestone)) events.Add(new(milestone, frame!.CompletedUtc, sample.SimulationSeconds));
        }
        // Completion is historical; disconnection still revokes current permission.
        if (Has(PlatformVisitMilestone.ReturnObserved))
            return Status(PlatformVisitStage.Completed, "Diagnostic visit complete once. No flown-route proof or payment.");
        if (aircraft is not null && frame is not null && frame.Aircraft != aircraft)
        {
            blocked = true; armed = false; lossStart = null; previous = null;
            return Status(PlatformVisitStage.Blocked, "Aircraft identity changed. Preserve recording; start a new diagnostic.");
        }
        var sample = deck.Observation;
        if (sample is null || frame?.Runtime is not { Running: true, Paused: false } ||
            frame.Aircraft.Model != "H125" || sample.Slewing || sample.SimulationRate != 1)
        {
            armed = false; lossStart = null; previous = null;
            return Status(Has(PlatformVisitMilestone.DepartureObserved) ? PlatformVisitStage.AwaitingReturn : PlatformVisitStage.AwaitingDeck,
                "Interrupted/unsupported observation. Current permission revoked; historical milestones are not live authority.");
        }
        aircraft ??= frame.Aircraft;
        var prior = previous; previous = sample;
        if (!sample.Consecutive) { armed = false; lossStart = null; }
        if (deck.DiagnosticDeckPresence)
        {
            if (Has(PlatformVisitMilestone.DepartureObserved))
            {
                Record(PlatformVisitMilestone.ReturnObserved, sample);
                return Status(PlatformVisitStage.Completed, "Diagnostic deck/contact-loss/return observed once. No flight, delivery or payment proof.");
            }
            Record(PlatformVisitMilestone.DeckEstablished, sample);
            armed = true; lossStart = null;
            return Status(PlatformVisitStage.ReadyForDeparture, "Deck established. Observe genuine contact loss; no repositioning counts as flown proof.");
        }
        if (Has(PlatformVisitMilestone.DepartureObserved))
            return Status(PlatformVisitStage.AwaitingReturn, "Contact loss observed; require 20 fresh seconds of stable deck contact to finish diagnostic.");
        if (!armed || prior is null)
            return Status(PlatformVisitStage.AwaitingDeck, "Departure needs a current, uninterrupted established deck baseline.");

        var delta = sample.SimulationSeconds - prior.SimulationSeconds;
        var displacement = WorldTracking.DistanceMeters(prior.Latitude, prior.Longitude, sample.Latitude, sample.Longitude);
        // Conservative diagnostic plausibility guards, never operational dispatch/performance limits.
        var horizontalLimit = Math.Max(prior.GroundSpeedKnots, sample.GroundSpeedKnots) * .514444 * delta + 1;
        var verticalLimit = (Math.Max(Math.Abs(prior.VerticalSpeedFeetPerSecond), Math.Abs(sample.VerticalSpeedFeetPerSecond)) * .3048 + .5) * delta + .5;
        if (!sample.Consecutive || displacement > horizontalLimit || Math.Abs(sample.HeightAboveDeckMeters - prior.HeightAboveDeckMeters) > verticalLimit ||
            sample.DistanceMeters > 30 || sample.HeightAboveDeckMeters is < .3 or > 15 ||
            sample.GroundSpeedKnots > 10 || Math.Abs(sample.VerticalSpeedFeetPerSecond) > 10 ||
            Math.Abs(sample.BankDegrees) > 10 || Math.Abs(sample.PitchDegrees) > 10)
        {
            armed = false; lossStart = null;
            return Status(PlatformVisitStage.AwaitingDeck, "Discontinuous/out-of-envelope movement. Re-establish deck; no departure inferred.");
        }
        if (sample.OnGround || sample.Contacts != 0 || sample.HeightAboveDeckMeters <= 1.5)
        {
            lossStart = null;
            return Status(PlatformVisitStage.ReadyForDeparture, "No sustained clear-contact separation above the deck; not a departure.");
        }
        lossStart ??= sample.SimulationSeconds;
        var duration = sample.SimulationSeconds - lossStart.Value;
        if (duration < 3)
            return Status(PlatformVisitStage.ContactLoss, "All reported contacts clear and above deck; observing 3 continuous simulation seconds.", duration);
        Record(PlatformVisitMilestone.DepartureObserved, sample);
        armed = false; lossStart = null;
        return Status(PlatformVisitStage.AwaitingReturn, "Sustained diagnostic contact loss observed. Not qualified flown takeoff/route evidence.", duration);
    }
}
