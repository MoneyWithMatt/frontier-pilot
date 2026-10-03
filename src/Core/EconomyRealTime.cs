namespace FrontierAirportBuilder.Core;

/// <summary>UTC wall-clock schedule persisted with the economy; never derived from simulator time.</summary>
public sealed record EconomyRealTimeState(DateTimeOffset StartedUtc, DateTimeOffset LastProcessedUtc,
    DateTimeOffset NextOffersUtc, DateTimeOffset NextProductionUtc, int OfferIntervalHours = 3, int OfferLifetimeHours = 6, int OfferSlotsPerDemand = 2)
{
    public void EnsureValid()
    {
        if (StartedUtc == default || LastProcessedUtc < StartedUtc || NextOffersUtc <= LastProcessedUtc ||
            NextProductionUtc <= LastProcessedUtc || OfferIntervalHours is < 1 or > 24 || OfferLifetimeHours < OfferIntervalHours || OfferLifetimeHours > 168 || OfferSlotsPerDemand is < 1 or > 5)
            throw new InvalidDataException("Real-time economy schedule is inconsistent. State preserved.");
    }
}

public static class EconomyRealTime
{
    public static EconomyTransition Advance(EconomyWorld source, DateTimeOffset utc)
    {
        var world = source.EnsureValid();
        if (utc == default) throw new ArgumentOutOfRangeException(nameof(utc));
        var clock = world.RealTime;
        if (clock is null)
        {
            clock = new(utc, utc, utc.AddHours(3), utc.AddDays(1));
            var systemNeeds = world.RecurringDemand.Select(x => x.NeedId).ToHashSet();
            var systemRequirements = world.Requirements.Where(x => systemNeeds.Contains(x.NeedId)).Select(x => x.Id).ToHashSet();
            world = world with { RealTime = clock, Jobs = world.Jobs.Select(x => x.State == EconomyJobState.Offered && systemRequirements.Contains(x.RequirementId)
                ? x with { OfferedUtc = utc, ExpiresUtc = utc.AddHours(clock.OfferLifetimeHours) } : x).ToArray() };

            return new((world with { Revision = source.Revision + 1 }).EnsureValid(), true);
        }
        // Reversing the computer clock cannot replay completed economy events.
        if (utc < clock.LastProcessedUtc) return new(world, false);
        var expired = world.Jobs.Where(x => x.State == EconomyJobState.Offered && x.ExpiresUtc <= utc).ToArray();
        var dueOffers = utc >= clock.NextOffersUtc;
        var dueProduction = utc >= clock.NextProductionUtc;
        if (expired.Length == 0 && !dueOffers && !dueProduction) return PlayerLogisticsKernel.ProcessRoad(world, utc);
        var expiredIds = expired.Select(x => x.Id).ToHashSet();
        var expiredRequirements = expired.Select(x => x.RequirementId).ToHashSet();
        world = world with
        {
            Jobs = world.Jobs.Select(x => expiredIds.Contains(x.Id) ? x with { State = EconomyJobState.Expired, CancelledUtc = x.ExpiresUtc, CancellationReason = "Unaccepted SYSTEM offer expired." } : x).ToArray(),
            Requirements = world.Requirements.Select(x => expiredRequirements.Contains(x.Id) ? x with { State = TransportRequirementState.Cancelled, CancelledUtc = expired.Single(j => j.RequirementId == x.Id).ExpiresUtc, CancellationReason = "Unaccepted SYSTEM offer expired." } : x).ToArray()
        };
        var production = clock.NextProductionUtc;
        // Only real elapsed days since activation are simulated; legacy saves get no retroactive consumption.
        // Bound each transaction to a year of catch-up, retaining the remaining backlog for the next refresh.
        for (var day = 0; production <= utc && day < 365; day++, production = production.AddDays(1))
        {
            world = PlayerLogisticsKernel.ProcessRoad(world, production).World;
            foreach (var operation in world.Operations.OrderBy(x => x.Id))
            {
                var site = world.Sites.Single(x => x.Id == operation.SiteId);
                if (site.FoundedUtc > production.AddDays(-1)) continue;
                world = ProspectingCampKernel.AdvanceDay(world, operation.Id,
                    EconomyIds.Derive("realtime-day-", operation.Id + "-" + production.UtcTicks), production).World;
            }
        }
        // Missed boards are not generated and discarded while offline: generate only the current board.
        var nextOffers = clock.NextOffersUtc;
        if (dueOffers) nextOffers = nextOffers.AddHours((Math.Floor((utc-nextOffers).TotalHours / clock.OfferIntervalHours)+1)*clock.OfferIntervalHours);
        var processed = production <= utc ? production.AddDays(-1) : utc;
        world = world with { RealTime = clock with { LastProcessedUtc = processed, NextOffersUtc = nextOffers, NextProductionUtc = production } };
        world = PlayerLogisticsKernel.ProcessRoad(world, processed).World;
        if (dueOffers) world = EconomyJobBoard.Maintain(world, utc, false, true).World;
        return new((world with { Revision = source.Revision + 1 }).EnsureValid(), true);
    }
}
