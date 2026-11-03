using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.TestFixtures;

/// <summary>Synthetic telemetry and temporary/memory stores only. Never MSFS acceptance.</summary>
public sealed class SyntheticOffshoreFlight
{
    public static readonly DateTimeOffset Epoch = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Utc = Epoch;
    public Guid? SelectedAirframe = Guid.NewGuid();
    public string CompanyType = "vision-jet";
    public IEconomyStateStore Store { get; }
    public OffshoreMissionsService Service { get; private set; }
    public readonly EmployerDestinationCatalogue Destinations = new([new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic fixture only; no MSFS acceptance", Epoch, ["vision-jet"])]);
    public static CareerEmployer Employer => CareerCatalogue.Employers.Single(e => e.Category == "Offshore");
    public readonly OffshoreOperatingArea Area = new("synthetic-area", "SYNTHETIC observation area", -23, 15.05, .5, 10);
    public SyntheticOffshoreFlight(IEconomyStateStore? store = null)
    {
        Store = store ?? new MemoryEconomyStateStore(); Service = CreateService();
    }
    public OffshoreMissionsService CreateService(Action<EconomyWorld>? history = null) => new(Store, Employer, Destinations,
        () => [new("SYNTHETIC Vision Jet", "SF50", "vision-jet")], () => SelectedAirframe, () => Utc, Area)
    {
        SelectedCompanyType = () => CompanyType,
        LiveIdentityGuard = i => i.ObjectId == 42,
        DispatchGuard = (_, _) => null,
        WorldObserved = history
    };
    public async Task Initialize()
    {
        await Service.RefreshAsync(); Service.SelectedId = Service.Missions.First(m => m.State == OffshoreMissionState.Offered).Id;
    }
    public ConditionFrame Frame(int second, double longitude = 15, bool ground = true, params (string Key, double Value)[] changes)
    {
        Utc = Epoch.AddSeconds(second);
        var identity = new ConditionAircraftIdentity(42, "SYNTHETIC Vision Jet", "SF50", 1);
        var profile = new OffshoreFlightProfile();
        var values = new Dictionary<string, double>
        {
            ["PLANE LATITUDE"] = -23, ["PLANE LONGITUDE"] = longitude, ["PLANE ALTITUDE"] = 500,
            ["GROUND ALTITUDE"] = ground ? 500 : 0, ["GROUND VELOCITY"] = ground ? 0 : 180,
            ["SIM ON GROUND"] = ground ? 1 : 0, ["SIMULATION RATE"] = 1, ["SIMULATION TIME"] = second
        };
        foreach (var pair in changes) values[pair.Key] = pair.Value;
        return new(Utc, Utc, identity, profile.Id, profile.Fields(identity).Select(f => f.Observe(values.GetValueOrDefault(f.Key), Utc)).ToArray())
        { Runtime = new(true, false, 0) };
    }
    public Task Observe(int second, double longitude = 15, bool ground = true, params (string Key, double Value)[] changes) =>
        Service.ObserveAsync(Frame(second, longitude, ground, changes));
    public async Task Begin()
    {
        await Initialize(); await Service.AcceptAsync();
        for (int i = 0; i <= 10; i++) await Observe(i);
        await Service.StartOrResumeAsync();
    }
    public async Task Outbound()
    {
        for (int i = 11; i <= 70; i++) await Observe(i, 15 + .05 * (i - 11) / 59, false);
        for (int i = 71; i <= 90; i++) await Observe(i, 15.05, false);
    }
    public async Task Return()
    {
        for (int i = 91; i <= 150; i++) await Observe(i, 15.05 - .05 * (i - 91) / 59, false);
        for (int i = 151; i <= 161; i++) await Observe(i);
    }
}
