using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.Desktop;
using FrontierAirportBuilder.TestFixtures;

internal static partial class Program
{
    private static async Task CheckCargoJourney(string folder)
    {
        var store=new MemoryEconomyStateStore();var physical=new SyntheticPassengerAircraft();var airframe=Guid.NewGuid();
        var catalogue=NamibiaLocationCatalogue.Load();
        PassengerEndpoint Endpoint(string id)
        {var l=catalogue.Get(id);var c=l.ApproximateCoordinates!;return new(id,l.Name,c.Latitude,c.Longitude);}
        PassengerEndpoint[] places=[Endpoint(EmployedCargoPrototype.ErosId),Endpoint(EmployedCargoPrototype.UisId),Endpoint(EmployedCargoPrototype.ArandisId)];
        var world=EmployedCargoPrototype.Create(catalogue,new(UisCargoKilograms:10,ArandisCargoKilograms:25));
        await store.InitializeAsync(world);
        var jobs=world.Jobs.ToArray();foreach(var job in jobs)await store.ApplyAsync(w=>EconomyKernel.AcceptJob(w,job.Id,physical.Sample.ObservedUtc));
        physical.Sample=physical.Sample with{Latitude=places[0].Latitude,Longitude=places[0].Longitude};
        var service=new CargoJourneyService(store,physical,()=>"synthetic",()=>airframe,(_,p)=>places.SingleOrDefault(e=>WorldTracking.DistanceMeters(e.Latitude,e.Longitude,p.Latitude,p.Longitude)<=2778),()=>physical.Sample.ObservedUtc,physical.WaitForRetentionAsync);
        var vm=new CargoJourneyViewModel(service,()=>Task.CompletedTask,_=>places,()=>"SYNTHETIC-CARGO");
        var control=new CargoJourneyControl{DataContext=vm};var host=new Window{Content=control,Width=1267,Height=854,ShowInTaskbar=false};host.Show();Pump();
        Button FindButton(string content)=>Descendants<Button>(control).Single(b=>b.Content?.ToString()==content);
        async Task Click(string content)
        {var b=FindButton(content);Check(b.Command?.CanExecute(null)==true,"Cargo command is bound: "+content);b.Command!.Execute(null);await Task.Delay(120);Pump();}
        await Click("Refresh aircraft & jobs");Check(vm.Consignments.Count==2,"Cargo control lists both accepted destinations");
        var cargoGrid=(ItemsControl)control.FindName("CargoConsignments");
        void ClickRow(string jobId,string action)
        {
            var item=vm.Consignments.Single(c=>c.JobId==jobId);cargoGrid.UpdateLayout();Pump();
            var b=Descendants<Button>(cargoGrid).Single(b=>b.DataContext==item&&(action=="Load"?b.Style==control.Resources["AssignmentCard"]:b.Content?.ToString()==action));
            b.BringIntoView();Pump();Check(b.IsVisible&&b.IsEnabled&&b.Focusable,"Consignment card/action is keyboard accessible: "+action);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        }
        void ClickStation(int index)
        {
            var choices=(ItemsControl)control.FindName("PickupStations");
            var button=Descendants<Button>(choices).Single(b=>b.DataContext is CargoStationRow s&&s.Index==index);
            button.BringIntoView();Pump();Check(button.IsVisible&&button.IsEnabled,"Actual station button is usable");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        }
        var outline=(System.Windows.Shapes.Path)control.FindName("CargoAircraftOutline");
        Check(outline.IsVisible&&!outline.IsHitTestVisible,"Aircraft outline is decorative and cannot intercept station clicks");
        var choices=(ItemsControl)control.FindName("PickupStations");
        Check(Descendants<Button>(choices).Count()==vm.Stations.Count,"Diagram represents every reported station once");
        Check(Descendants<TextBlock>(control).Any(t=>t.Text.Contains("not physical compartment positions")),"Schematic explicitly distinguishes station order from geometry");
        // Presentation-only maximum-size fixture; these extra rows never enter the physical service snapshot.
        var extra=Enumerable.Range(7,9).Select(i=>new CargoStationRow{Index=i,Name=i==15?"Synthetic rear cargo compartment":"Synthetic station "+i,Actual=i==15?123.456m:0,Writable=i!=15}).ToArray();
        foreach(var row in extra)vm.Stations.Add(row);Pump();
        var buttons=Descendants<Button>(choices).ToArray();
        Check(buttons.Length==15&&!buttons.Single(b=>b.DataContext==extra[^1]).IsEnabled,"Fifteen station diagram retains read-only station safeguard");
        Check(buttons.Select(b=>((CargoStationRow)b.DataContext).Index).SequenceEqual(Enumerable.Range(1,15)),"Diagram preserves reported numeric station order");
        foreach(var button in buttons)
        {
            var box=button.TransformToAncestor(choices).TransformBounds(new Rect(0,0,button.ActualWidth,button.ActualHeight));
            Check(box.Left>=0&&box.Right<=choices.ActualWidth+.1,"Station box fits the diagram width");
        }
        choices.BringIntoView();Pump();Save(host,Path.Combine(folder,"cargo-aircraft-diagram-15.png"));
        foreach(var row in extra)vm.Stations.Remove(row);Pump();
        Save(host,Path.Combine(folder,"cargo-cards-none.png"));
        var first=vm.Consignments[0].JobId;ClickRow(first,"Load");
        var selectedButton=Descendants<Button>(cargoGrid).Single(b=>b.DataContext==vm.PickupCargo&&b.Style==control.Resources["AssignmentCard"]);
        Check(((System.Windows.Media.SolidColorBrush)selectedButton.Background).Color==System.Windows.Media.Color.FromRgb(34,59,75),"Selected cargo uses blue card background");
        Check(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(selectedButton)),"Cargo card has accessible state description");
Check(vm.IsChoosingStation,"Load opens the station chooser");
        Save(host,Path.Combine(folder,"cargo-station-picker.png"));
        var partial=Descendants<Expander>(control).Single(e=>e.Header?.ToString()=="Partial amount / split");partial.IsExpanded=true;Pump();
        var amount=(TextBox)control.FindName("PickupQuantity");Check(amount.IsVisible,"Contextual partial quantity is visible when requested");amount.Text="invalid";amount.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();ClickStation(6);
        Check(vm.Pickups.Count==0&&vm.IsChoosingStation,"Invalid text cannot silently load the previous amount");
        amount.Text=vm.Consignments.Single(c=>c.JobId==first).AvailableHere.ToString();amount.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();ClickStation(6);
        Check(!vm.IsChoosingStation&&vm.Pickups.Count==1,"Full pickup chooses station without quantity arithmetic");partial.IsExpanded=false;Pump();
        var change=Descendants<Button>((ItemsControl)control.FindName("PickupPlan")).Single(b=>b.Content?.ToString()=="Change");change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();ClickStation(5);
        Check(vm.Pickups.Single().StationIndex==5&&physical.Weights[4]==0,"Change reassigns only the proposed station, no simulator write");
        change=Descendants<Button>((ItemsControl)control.FindName("PickupPlan")).Single(b=>b.Content?.ToString()=="Change");change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();ClickStation(6);
        var paired=vm.Consignments.Single(c=>c.JobId==first);
        var pairedButton=Descendants<Button>(cargoGrid).Single(b=>b.DataContext==paired&&b.Style==control.Resources["AssignmentCard"]);
        var stationButton=Descendants<Button>((ItemsControl)control.FindName("PickupStations")).Single(b=>b.DataContext is CargoStationRow row&&row.Index==6);
        Check(pairedButton.Background.ToString()==stationButton.Background.ToString()&&paired.VisualState=="Paired","Cargo and station share green proposed state");
        Check(vm.SelectedStationDetails.Contains("limit 500")&&vm.SelectedStationDetails.Contains("General Supplies"),"Station selection retains reported limit and consignment details outside compact boxes");
        Save(host,Path.Combine(folder,"cargo-cards-paired.png"));
        var remove=Descendants<Button>((ItemsControl)control.FindName("PickupPlan")).Single(b=>b.Content?.ToString()=="Remove");remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(vm.RequestedKilograms==0,"Remove undoes only the proposed pickup");
        foreach(var id in vm.Consignments.Select(c=>c.JobId).ToArray()){ClickRow(id,"Load");ClickStation(6);}
        Check(vm.Pickups.Count==2&&vm.RequestedKilograms==35,"Two consignment station choices are previewed independently");
        Save(host,Path.Combine(folder,"cargo-click-to-load-plan.png"));

        await Click("Confirm loading / unloading plan");Check(vm.ManifestText.Contains("PROPOSED / PENDING"),"Pending printout is distinct from committed cargo: "+vm.Status);
        Check(physical.Weights[5]==33,"Preview does not write the simulator");Check(!cargoGrid.IsEnabled,"Pending plan locks editable consignment controls");Check(Descendants<Expander>(control).All(e=>!e.IsExpanded),"Station and recovery details are collapsed in the simple flow");Save(host,Path.Combine(folder,"cargo-dispatch-proposed.png"));
        await Click("Apply cargo in cockpit");Check(physical.Weights[5]==68&&service.Manifest!.Cargo.Length==2,"Real bound apply command confirms 35 kg: "+vm.Status);
        foreach(var item in vm.Consignments)
        {
            var button=Descendants<Button>(cargoGrid).Single(b=>b.DataContext==item&&b.Style==control.Resources["AssignmentCard"]);
            var destination=Descendants<TextBlock>(button).Single(t=>t.Text=="TO: "+item.Destination);
            var bounds=destination.TransformToAncestor(button).TransformBounds(new Rect(0,0,destination.ActualWidth,destination.ActualHeight));
            Check(bounds.Bottom<=button.ActualHeight,"Cargo card destination is not clipped");
        }
        Check(vm.ManifestText.Contains("TOTAL COMMITTED CARGO: 35"),"Printout reflects committed 35 kg");Save(host,Path.Combine(folder,"cargo-dispatch-35kg.png"));
        var loadingScroll=Descendants<ScrollViewer>(control).Single(s=>s.Content is StackPanel);loadingScroll.ScrollToBottom();Pump();
        Save(host,Path.Combine(folder,"cargo-stations-and-actions.png"));loadingScroll.ScrollToTop();Pump();
        async Task Observe(PassengerEndpoint p,bool ground)
        {
            physical.Sample=physical.Sample with{ObservedUtc=physical.Sample.ObservedUtc.AddSeconds(1),Latitude=p.Latitude,Longitude=p.Longitude,IsOnGround=ground,GroundSpeedKnots=ground?0:100};
            await vm.ObserveAsync(physical.Sample);Pump();
        }
        async Task FlyTo(PassengerEndpoint origin,PassengerEndpoint destination)
        {
            var stops=(ComboBox)control.FindName("CargoDestination");stops.SelectedItem=vm.Stops.Single(x=>x.Endpoint.Id==destination.Id);Pump();
            await Click("Prepare departure");for(var i=0;i<=10;i++)await Observe(origin,true);await Click("Start flight recording");
            for(var i=1;i<=12;i++)await Observe(origin with{Latitude=origin.Latitude+(destination.Latitude-origin.Latitude)*i/12,Longitude=origin.Longitude+(destination.Longitude-origin.Longitude)*i/12},false);
            for(var i=0;i<=10;i++)await Observe(destination,true);
            Check(service.Manifest!.Legs.Last().SettledUtc is not null,"Cargo leg qualifies through production tracker: "+service.Reason);
        }
        await FlyTo(places[0],places[1]);await Click("Refresh aircraft & jobs");
        var a=vm.Consignments.Single(x=>x.Status=="DELIVER HERE");
        var details=Descendants<Expander>(control).Single(e=>e.Header?.ToString()=="Station distribution / details");details.IsExpanded=true;Pump();
        var overrides=(DataGrid)control.FindName("CargoQuantityOverrides");overrides.ScrollIntoView(a,overrides.Columns[2]);overrides.UpdateLayout();overrides.CurrentCell=new(a,overrides.Columns[2]);overrides.Focus();Check(overrides.BeginEdit(),"Partial-delivery override is still editable");Pump();
        var edit=Descendants<TextBox>(overrides).Single(t=>t.IsVisible);edit.Text="5";overrides.CommitEdit(DataGridEditingUnit.Cell,true);overrides.CommitEdit(DataGridEditingUnit.Row,true);Pump();
        details.IsExpanded=false;Pump();await Click("Confirm loading / unloading plan");await Click("Apply cargo in cockpit");
        Check(physical.Weights[5]==63&&service.Manifest!.Cargo.Sum(c=>c.Kilograms)==30,"Partial UI delivery retains 5 kg plus 25 kg through-cargo");
        ClickRow(a.JobId,"Deliver");
        await Click("Confirm loading / unloading plan");await Click("Apply cargo in cockpit");
        Check(physical.Weights[5]==58&&service.Manifest!.Cargo.Single().Kilograms==25,"First UI delivery retains unrelated 25 kg");
        Check(vm.ManifestText.Contains("TOTAL COMMITTED CARGO: 25"),"In-flight dispatch sheet updates after partial journey");Save(host,Path.Combine(folder,"cargo-dispatch-through-cargo.png"));
        await FlyTo(places[1],places[2]);await Click("Refresh aircraft & jobs");ClickRow(vm.Consignments.Single().JobId,"Deliver");
        await Click("Confirm loading / unloading plan");await Click("Apply cargo in cockpit");
        Check(physical.Weights[5]==33&&service.Manifest!.Cargo.Length==0,"Second UI delivery restores unrelated station baseline");
        Check((await store.LoadAsync())!.Jobs.Count(j=>j.State==EconomyJobState.Completed)==2,"Both UI consignments complete independently");
        Save(host,Path.Combine(folder,"cargo-dispatch-delivered.png"));host.Close();
    }
}
