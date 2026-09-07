using System.ComponentModel;
using System.Globalization;
using FrontierAirportBuilder.Core;
namespace FrontierAirportBuilder.Workflow;

public sealed class ParkingStandInput(int number, Func<bool> canEdit, double right) : INotifyPropertyChanged
{
    public int Number { get; } = number;
    private string forward="0", side=right.ToString(CultureInfo.InvariantCulture), facing="0", radius="10";
    public string Forward { get=>forward;set=>Set(ref forward,value,nameof(Forward)); }
    public string Right { get=>side;set=>Set(ref side,value,nameof(Right)); }
    public string Facing { get=>facing;set=>Set(ref facing,value,nameof(Facing)); }
    public string Radius { get=>radius;set=>Set(ref radius,value,nameof(Radius)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set(ref string field,string value,string name) { if(!canEdit()||field==value)return;field=value;PropertyChanged?.Invoke(this,new(name)); }
    public ParkingStand ToDefinition()
    {
        double Parse(string value) => double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n) ? n : throw new ArgumentException($"Parking {Number}: enter numbers using a decimal point.");
        return new(Number,Parse(forward),Parse(side),Parse(facing),Parse(radius));
    }
}
public sealed record ParkingPreviewItem(int Number,double Left,double Top,double Diameter,double Facing);
