using Mapsui.Extensions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Diagnostics;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;
public partial class CareerScreen : UserControl
{
    private CareerViewModel? model;
    private CareerStage? lastStage;
    public event Action? DeleteRequested;
    public event Action? EmploymentAccepted;
    public event Action? DestinationsRequested;
    public CareerScreen(){InitializeComponent();Map.LocationSelected+=SelectLocation;}
    public void Show(CareerViewModel profile)
    {model=profile;DataContext=profile;model.PropertyChanged+=(_,_)=>Update();Update();}
    public void ShowError(string message){ErrorText.Text=message;Welcome.Visibility=NameStep.Visibility=MapStep.Visibility=Visibility.Collapsed;BackButton.IsEnabled=false;}
    private void Update()
    {
        if(model is null)return;
        Welcome.Visibility=model.Stage==CareerStage.New?Visibility.Visible:Visibility.Collapsed;
        NameStep.Visibility=model.Stage==CareerStage.PilotName?Visibility.Visible:Visibility.Collapsed;
        MapStep.Visibility=model.Stage is CareerStage.Region or CareerStage.Opportunities?Visibility.Visible:Visibility.Collapsed;
        RegionStep.Visibility=model.Stage==CareerStage.Region?Visibility.Visible:Visibility.Collapsed;
        EmployerStep.Visibility=model.Stage==CareerStage.Opportunities?Visibility.Visible:Visibility.Collapsed;
        BackButton.Visibility=model.Stage==CareerStage.New?Visibility.Collapsed:Visibility.Visible;
        StepTitle.Text=model.Stage switch{CareerStage.New=>"New Career",CareerStage.PilotName=>"Begin Your Career",CareerStage.Region=>"Choose a Region",_=>model.SelectedRegion?.Name??"Employment"};
        if(lastStage==model.Stage)return;lastStage=model.Stage;
        if(model.Stage==CareerStage.Region)
            Map.Show(model.Regions.Select(r=>Location(r.Id,r.Name,r.Name,r.Latitude,r.Longitude)).ToArray());
        if(model.Stage==CareerStage.Opportunities)
        {
            var bases=CareerCatalogue.Bases.Where(b=>model.Employers.Any(e=>e.BaseId==b.Id)).ToArray();
            Map.Show(bases.Select(b=>Location(b.Id,b.Name,b.Name,b.Latitude,b.Longitude)).ToArray());
            if(model.SelectedEmployer is null)model.SelectedEmployer=model.Employers.FirstOrDefault();
        }
        if(model.Stage is CareerStage.Region or CareerStage.Opportunities && model.SelectedRegion is {} region)
            Map.Map.Navigator.CenterOnAndZoomTo(Mapsui.Projections.SphericalMercator.FromLonLat(region.Longitude,region.Latitude).ToMPoint(),region.MapResolution);
    }
    private static OperationsMapLocation Location(string id,string name,string label,double lat,double lon)=>
        new(id,name,label,OperationsMapLocationKind.SystemLocation,"Career opportunity","Planned",lat,lon,"",[],[],null);
    private void SelectLocation(string id)
    {
        if(model is null)return;
        if(model.Stage==CareerStage.Region)model.SelectedRegion=model.Regions.FirstOrDefault(r=>r.Id==id);
        else {var employers=model.Employers.Where(e=>e.BaseId==id).ToArray();model.SelectedEmployer=employers.FirstOrDefault();BaseHint.Text=string.Join(" · ",employers.Select(e=>e.Name));}
    }
    private void Continue(object sender,RoutedEventArgs e){try{if(model?.Continue()!=true)ErrorText.Text=model?.Stage==CareerStage.Region?"Choose a region.":model?.NameError;else ErrorText.Text="";}catch(Exception ex){ErrorText.Text=ex.Message;}}
    private void Back(object sender,RoutedEventArgs e){try{model?.Back();ErrorText.Text="";}catch(Exception ex){ErrorText.Text=ex.Message;}}
    private void Delete(object sender,RoutedEventArgs e)=>DeleteRequested?.Invoke();
    private void AcceptEmployment(object sender,RoutedEventArgs e)
    {try{model?.AcceptEmployment();EmploymentAccepted?.Invoke();}catch(Exception ex){ErrorText.Text=ex.Message;}}
    private void InspectDestinations(object sender,RoutedEventArgs e)=>DestinationsRequested?.Invoke();
    private void Attribution(object sender,RequestNavigateEventArgs e){Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}
}
