using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Desktop;

public sealed class DestinationDiagnosticsWindow : Window
{
    private readonly DestinationReviewStore store;
    private EmployerDestinationCatalogue catalogue=new();
    private readonly ComboBox category=new(){ItemsSource=new[]{"All","Missionary","Industrial","Safari","Offshore"},SelectedIndex=0,Width=180};
    private readonly DataGrid rows=new(){IsReadOnly=true,AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Single,MinHeight=180};
    private readonly ComboBox status=new(){ItemsSource=new[]{"Needs investigation","Approved","Rejected","Needs scenery"},SelectedIndex=0};
    private readonly TextBox identifier=new(),latitude=new(),longitude=new(),evidence=new(){TextWrapping=TextWrapping.Wrap,MinHeight=50};
    private readonly ListBox aircraft=new(){ItemsSource=CompanyAircraft.Types.Where(x=>x.Enabled).ToArray(),SelectionMode=SelectionMode.Multiple,Height=90};
    private readonly TextBlock detail=new(){TextWrapping=TextWrapping.Wrap},message=new(){TextWrapping=TextWrapping.Wrap};
    public DestinationDiagnosticsWindow(string root, string sdkRoot="", Action<SimulatorAirportCache>? applied=null, bool allowLive=true)
    {
        store=new(root);Title="Curated destinations — MSFS inspection";Width=1180;Height=850;MinWidth=850;MinHeight=650;
        var grid=new Grid{Margin=new Thickness(18)};Content=grid;
        grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel();header.Children.Add(new TextBlock{Text="Check all airports with a flight loaded anywhere in MSFS. Found airports enable company jobs automatically; missing airports are flagged. Runway suitability remains unassessed. Manual reviews are optional and apply after restart.",TextWrapping=TextWrapping.Wrap});
        var check=new Button{Content="Check all airports in MSFS",HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=allowLive};header.Children.Add(check);
        var cancel=new CancellationTokenSource();Closed+=(_,_)=>cancel.Cancel();
        check.Click+=async (_,_)=>
        {
            check.IsEnabled=false;message.Text="Reading the complete MSFS airport catalogue…";
            try
            {
                var scan=await LiveAirportCatalogue.ReadAsync(sdkRoot,cancel.Token);
                var cache=new SimulatorAirportStore(root).Save(scan);
                catalogue=store.Load();applied?.Invoke(cache);RefreshRows();
                var missing=catalogue.Diagnose().Where(x=>x.Recognition.Contains("Not found")||x.Recognition.Contains("Ambiguous")||x.Recognition.Contains("Identifier missing")).Select(x=>x.Research.Key).Distinct().Count();
                message.Text=$"Airport check saved: {cache.Scan.ObservedUtc:u}. {missing} destinations need attention. Found routes are available immediately; no individual approval needed.";
            }
            catch(Exception ex){message.Text="Lookup incomplete; previous results preserved. "+ex.Message;}
            finally{check.IsEnabled=true;}
        };
        header.Children.Add(category);grid.Children.Add(header);
        foreach(var column in new[]{("Employer","Employer"),("Airport","Airport"),("Identifier","Identifier"),("Recognition","Recognition"),("Jobs enabled","Enabled"),("Runway research","Runway")})
            rows.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new Binding(column.Item2),Width=new DataGridLength(column.Item2=="Airport"||column.Item2=="Recognition"||column.Item2=="Runway"?2:1,DataGridLengthUnitType.Star)});
        Grid.SetRow(rows,1);grid.Children.Add(rows);
        var form=new StackPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(form,2);grid.Children.Add(form);form.Children.Add(detail);
        var fields=new UniformGridShim();fields.AddField("Review",status);fields.AddField("Inspected identifier",identifier);fields.AddField("MSFS latitude (decimal)",latitude);fields.AddField("MSFS longitude (decimal)",longitude);form.Children.Add(fields);
        form.Children.Add(new TextBlock{Text="Inspection evidence: simulator/version, date, airport/runway observations and coordinate source."});form.Children.Add(evidence);
        form.Children.Add(new TextBlock{Text="Optional curated aircraft compatibility (Ctrl+click). No selections = unassessed, pilot must check suitability. H125 unavailable."});form.Children.Add(aircraft);
        var save=new Button{Content="Save inspection / review…",HorizontalAlignment=HorizontalAlignment.Left};save.Click+=Save;form.Children.Add(save);form.Children.Add(message);
        category.SelectionChanged+=(_,_)=>RefreshRows();rows.SelectionChanged+=(_,_)=>Select();
        try{catalogue=store.Load();RefreshRows();}catch(Exception ex){message.Text=ex.Message;save.IsEnabled=false;}
    }
    private void RefreshRows()=>rows.ItemsSource=catalogue.Diagnose(category.SelectedIndex==0?null:category.SelectedItem?.ToString());
    private void Select()
    {
        if(rows.SelectedItem is not DestinationDiagnostic row)return;
        var prior=catalogue.Reviews.SingleOrDefault(x=>x.Key==row.Research.Key);
        detail.Text=row.Notes+"\nProposed work: "+row.Research.ProposedMissions+" · Source: "+row.Research.SourceUrl;
        status.SelectedItem=prior?.Status??"Needs investigation";identifier.Text=prior?.Identifier??row.Identifier;
        latitude.Text=(prior?.Latitude??row.Resolved?.ApproximateCoordinates?.Latitude)?.ToString(CultureInfo.InvariantCulture)??"";
        longitude.Text=(prior?.Longitude??row.Resolved?.ApproximateCoordinates?.Longitude)?.ToString(CultureInfo.InvariantCulture)??"";
        evidence.Text=prior?.Evidence??"";aircraft.SelectedItems.Clear();foreach(var type in CompanyAircraft.Types.Where(x=>prior?.CompatibleAircraft.Contains(x.Id)==true))aircraft.SelectedItems.Add(type);
    }
    private void Save(object sender,RoutedEventArgs args)
    {
        if(rows.SelectedItem is not DestinationDiagnostic row)return;
        try
        {
            var approved=status.SelectedItem?.ToString()=="Approved";
            var prior=catalogue.Reviews.SingleOrDefault(x=>x.Key==row.Research.Key);
            double? lat=prior?.Latitude,lon=prior?.Longitude;
            if(approved){lat=double.Parse(latitude.Text,CultureInfo.InvariantCulture);lon=double.Parse(longitude.Text,CultureInfo.InvariantCulture);}
            var review=new DestinationReview(row.Research.Key,status.SelectedItem!.ToString()!,identifier.Text.Trim().ToUpperInvariant(),lat,lon,evidence.Text.Trim(),DateTimeOffset.UtcNow,aircraft.SelectedItems.Cast<CompanyAircraftType>().Select(x=>x.Id).ToArray());
            EmployerDestinationCatalogue.ValidateReview(review);
            if(MessageBox.Show(this,approved?"Confirm you personally inspected this airport in MSFS and recorded its actual identifier and coordinates. This enables eligible company routes after restart. Aircraft compatibility is your explicit curation, not a performance calculation.":"Save this review? Unapproved destinations are excluded from new company offers after restart. Accepted jobs and history are retained.","Record MSFS inspection",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            store.Save(review);catalogue=store.Load();RefreshRows();message.Text="Saved. Restart Frontier Pilot to apply this catalogue revision to jobs. Career reset preserves inspections.";
        }
        catch(Exception ex){message.Text=ex.Message;}
    }
    private sealed class UniformGridShim : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGridShim(){Columns=4;Margin=new Thickness(0,8,0,8);}
        public void AddField(string label,Control control){var panel=new StackPanel{Margin=new Thickness(0,0,8,0)};panel.Children.Add(new TextBlock{Text=label});panel.Children.Add(control);Children.Add(panel);}
    }
}
