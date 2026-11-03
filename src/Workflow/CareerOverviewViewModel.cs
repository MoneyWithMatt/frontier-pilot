using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class CareerOverviewViewModel(CareerProfile? profile, CareerEmployer? employer, AircraftFleetViewModel fleet)
{
    public string Pilot => profile?.PilotName is { Length: > 0 } name ? name : "Pilot";
    public string Company => employer?.Name ?? "Independent / legacy career";
    public string Monogram => employer is null ? "FP" : string.Concat(employer.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(s => s[0]));
    public string LogoDescription => "Company initials · placeholder, not an official logo";
    public string Description => employer?.Description ?? "Your existing personal career and aircraft records remain available. Dedicated freelance and aviation-company management are not yet implemented.";
    public string Role => profile?.Employment?.Role ?? "Independent pilot · existing career";
    public string Base => employer is null ? "No employer base assigned" : CareerCatalogue.Bases.FirstOrDefault(b => b.Id == employer.BaseId) is {} b ? $"{b.Name} ({b.Identifier}) · assigned operating base" : "Base unavailable";
    public string Pay => employer is null ? "Existing contracts retain their recorded terms" : DollarDisplay.Format(employer.HourlyWage) + " per verified airborne hour";
    public string Activities => employer?.Work ?? "Existing cargo contracts and owned-site operations where authorised";
    public string Employment => employer is null ? "No NPC employment recorded in this profile" : "Employed · " + employer.Name;
    public string Fleet => fleet.Aircraft.Count == 0 ? "No authorised physical aircraft records" :
        string.Join("\n", fleet.Aircraft.Select(a => $"{a.AllocatedTypeName ?? a.Identity.SimulatorModel} · {a.Registration} · {a.Availability}"));
}

public sealed record CareerEmployerListing(CareerEmployer Employer, bool Current)
{
    public string Name => Employer.Name;
    public string Description => Employer.Description;
    public string Role => Employer.Role;
    public string Base => CareerCatalogue.Bases.First(b => b.Id == Employer.BaseId) is {} b ? $"{b.Name} ({b.Identifier})" : "Unavailable";
    public string Pay => DollarDisplay.Format(Employer.HourlyWage) + " / verified airborne hour";
    public string Aircraft => Employer.Aircraft;
    public string Work => Employer.Work;
    public string Status => Current ? "Current employer" : "Browse only · active employment switching unavailable";
}
public sealed class CareerWorkViewModel(CareerEmployer? employer)
{
    public IReadOnlyList<CareerEmployerListing> Employers { get; } = CareerCatalogue.Employers.Select(e => new CareerEmployerListing(e, e.Id == employer?.Id)).ToArray();
    public string Switching => "Changing an active employer is not safely supported yet. Accepted work, payload ownership, unpaid flight evidence and company aircraft must be reconciled before leaving. Browsing does not change employment or reset your career.";
    public string PlayerCompanies => "Player-owned employer vacancies and applications are planned. No multiplayer hiring or employment-agreement backend is available.";
}

public sealed record CareerAircraftRow(PhysicalAirframe Airframe, CareerEmployer? Employer)
{
    public Guid Id => Airframe.Id;
    public string Type => Airframe.AllocatedTypeName ?? Airframe.Identity.SimulatorModel;
    public string Registration => Airframe.Registration;
    public string Ownership => Airframe.OwnerEntityId == Employer?.Id && Employer is not null ? Employer.Name + " · company property" :
        Airframe.OwnerEntityId is null ? "Personal / legacy record · ownership not inferred" : Airframe.OwnerEntityId;
    public string Assignment => Airframe.AssignedPlayerId is null ? "No company assignment recorded" : "Assigned to this player profile";
    public string Base => Airframe.AllocatedBase is {} b ? $"{b.Name} ({b.Identifier}) · assigned base, not observed location" : "No assigned base recorded";
    public string Location => Airframe.Latest is null ? "No simulator location observation" : Airframe.LocationLabel + " · last known, not parking authority";
    public string Restrictions => CompanyAircraft.Types.SingleOrDefault(t => t.Id == Airframe.CompanyTypeId)?.Enabled == false ? "Coming soon · mission capability disabled" : Airframe.Availability;
    public string Binding => Airframe.Bindings.Count == 0 ? "No confirmed simulator binding" : string.Join("\n", Airframe.Bindings.Select(b => b.Title + " / " + b.Model));
}
