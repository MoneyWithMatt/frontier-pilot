using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrontierAirportBuilder.Workflow;

public enum CareerSection { Overview, Fleet, FindWork, JobBoard, CreateJobs }
public enum CareerAircraftSection { Aircraft, Condition, LoadManager }
public enum CareerLoadingSection { Cargo, Passengers, AircraftSystems }
/// <summary>Transient navigation only. Domain records and physical identity remain in their existing stores.</summary>
public sealed class CareerNavigation : INotifyPropertyChanged
{
    private int section, aircraftSection, loadingSection;
    public int Section { get => section; set { if (value is >= 0 and <= 4 && section != value) { section = value; Changed(); } } }
    public int AircraftSection { get => aircraftSection; set { if (value is >= 0 and <= 2 && aircraftSection != value) { aircraftSection = value; Changed(); } } }
    public int LoadingSection { get => loadingSection; set { if (value is >= 0 and <= 2 && loadingSection != value) { loadingSection = value; Changed(); } } }
    public string? SelectedJobKey { get; internal set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
