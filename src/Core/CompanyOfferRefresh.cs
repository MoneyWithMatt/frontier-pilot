namespace FrontierAirportBuilder.Core;

public sealed record CompanyOfferRefreshReceipt(string Id,string EmployerId,string PlayerId,long Sequence,DateTimeOffset RecordedUtc);

/// <summary>Only unpublished-to-the-pilot SYSTEM offers are replaced. No shipment, proof or money authority.</summary>
public static class CompanyOfferRefresh
{
    public static long Sequence(EconomyWorld w,string employer)=>w.CompanyOfferRefreshes.Where(r=>r.EmployerId==employer).Select(r=>r.Sequence).DefaultIfEmpty(0).Max();
    public static void Validate(EconomyWorld w)
    {
        EconomyValidation.Collection(w.CompanyOfferRefreshes,nameof(w.CompanyOfferRefreshes));
        if(w.CompanyOfferRefreshes.Any(r=>r is null))throw new InvalidDataException("Null company refresh receipt.");
        if(w.CompanyOfferRefreshes.Select(r=>r.Id).Distinct().Count()!=w.CompanyOfferRefreshes.Count)throw new InvalidDataException("Duplicate company board refresh receipt.");
        foreach(var r in w.CompanyOfferRefreshes)
        {
            EconomyValidation.Id(r.Id,"board refresh ID");
            if(r.RecordedUtc==default||r.Sequence<1||!w.Players.Any(p=>p.Id==r.PlayerId&&p.EmployerBusinessId==r.EmployerId)||
                !w.Businesses.Any(b=>b.Id==r.EmployerId&&b.Owner==EconomicOwnerKind.System))throw new InvalidDataException("Invalid company board refresh receipt.");
        }
        foreach(var group in w.CompanyOfferRefreshes.GroupBy(r=>r.EmployerId))
            if(!group.Select(r=>r.Sequence).Order().SequenceEqual(Enumerable.Range(1,group.Count()).Select(i=>(long)i)))throw new InvalidDataException("Company refresh sequence is inconsistent.");
    }
    public static EconomyTransition ReplaceAvailable(EconomyWorld source,string employer,string player,string requestId,DateTimeOffset utc)
    {
        var w=source.EnsureValid();EconomyValidation.Id(requestId,"board refresh request");
        if(!w.Players.Any(p=>p.Id==player&&p.EmployerBusinessId==employer)||!w.Businesses.Any(b=>b.Id==employer&&b.Owner==EconomicOwnerKind.System))
            throw new InvalidOperationException("Only the current NPC employer's available work can be refreshed.");
        if(w.CompanyOfferRefreshes.FirstOrDefault(r=>r.Id==requestId) is {} prior)
        {if(prior.EmployerId!=employer||prior.PlayerId!=player)throw new InvalidOperationException("Refresh request belongs to another employer/player.");return new(w,false);}
        if(utc==default||w.RealTime is null||utc<w.RealTime.LastProcessedUtc)throw new InvalidOperationException("Refresh needs a valid current economy clock.");
        var ids=w.Jobs.Where(j=>j.State==EconomyJobState.Offered&&j.EmployerBusinessId==employer&&j.PlayerId==player&&j.ExpiresUtc is not null&&
            !w.LogisticsContracts.Any(c=>c.JobId==j.Id)&&w.RecurringDemand.Any(d=>d.EmployerBusinessId==employer&&d.PlayerId==player&&d.NeedId==w.Requirements.Single(r=>r.Id==j.RequirementId).NeedId)).Select(j=>j.Id).ToHashSet();
        var requirements=w.Jobs.Where(j=>ids.Contains(j.Id)).Select(j=>j.RequirementId).ToHashSet();
        const string reason="Unaccepted company offer replaced by pilot board refresh.";
        var updated=w with {
            Revision=w.Revision+1,
            Jobs=w.Jobs.Select(j=>ids.Contains(j.Id)?j with {State=EconomyJobState.Expired,CancelledUtc=utc,CancellationReason=reason}:j).ToArray(),
            Requirements=w.Requirements.Select(r=>requirements.Contains(r.Id)?r with {State=TransportRequirementState.Cancelled,CancelledUtc=utc,CancellationReason=reason}:r).ToArray(),
            PassengerTransfers=w.PassengerTransfers.Select(t=>t.EmployerId==employer&&t.PlayerId==player&&t.State==PassengerTransferState.Offered?t with {State=PassengerTransferState.Expired}:t).ToArray(),
            OffshoreMissions=w.OffshoreMissions.Select(m=>m.EmployerId==employer&&m.PlayerId==player&&m.State==OffshoreMissionState.Offered?m with {State=OffshoreMissionState.Expired}:m).ToArray(),
            CompanyOfferRefreshes=w.CompanyOfferRefreshes.Append(new(requestId,employer,player,checked(Sequence(w,employer)+1),utc)).ToArray()
        };
        return new(updated.EnsureValid(),true);
    }
}
