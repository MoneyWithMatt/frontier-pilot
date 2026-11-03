using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class CareerFleetViewModel : INotifyPropertyChanged
{
    private readonly CareerEmployer? employer;
    private readonly Func<Guid, bool> select;
    public AircraftFleetViewModel Source { get; }
    public CareerFleetViewModel(AircraftFleetViewModel source, CareerEmployer? employer, Func<Guid, bool> select)
    { Source = source; this.employer = employer; this.select = select; }
    public IReadOnlyList<CareerAircraftRow> Aircraft => Source.Aircraft.Select(a => new CareerAircraftRow(a, employer)).ToArray();
    public Guid? SelectedId { get => Source.Selected?.Id; set { if (value is {} id) select(id); } }
    public CareerAircraftRow? Selected => Aircraft.FirstOrDefault(a => a.Id == SelectedId);
    public string Identity => Source.Selected is {} a ? $"Persistent airframe: {a.Id}\n{a.Identity.SimulatorTitle} / {a.Identity.SimulatorModel}" : "Select an aircraft to inspect its identity and loading workspace.";
    public string MaintenancePolicy => employer is not null ? "Employee access: inspect condition and maintenance history. Company repair authority is not granted. No qualified production maintenance facility is enabled." :
        "Condition and maintenance records are available for inspection. No qualified production maintenance facility is enabled; no repair or healthy-condition reset is offered here.";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify() => PropertyChanged?.Invoke(this, new(""));
}
