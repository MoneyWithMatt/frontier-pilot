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
        var cargoGrid=(DataGrid)control.FindName("CargoConsignments");
        void ClickRow(string jobId,string action)
        {
            var item=vm.Consignments.Single(c=>c.JobId==jobId);cargoGrid.ScrollIntoView(item);cargoGrid.UpdateLayout();Pump();
            var b=Descendants<Button>(cargoGrid).Single(b=>b.DataContext==item&&b.Content?.ToString()==action);
            Check(b.IsVisible&&b.IsEnabled,"Consignment action is usable: "+action);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        }
        void ClickStation(int index)
        {
            var choices=(ItemsControl)control.FindName("PickupStations");
            var button=Descendants<Button>(choices).Single(b=>b.DataContext is CargoStationRow s&&s.Index==index);
            button.BringIntoView();Pump();Check(button.IsVisible&&button.IsEnabled,"Actual station button is usable");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        }
        var first=vm.Consignments[0].JobId;ClickRow(first,"Load");Check(vm.IsChoosingStation,"Load opens the station chooser");
        var amount=(TextBox)control.FindName("PickupQuantity");amount.Text="invalid";amount.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();ClickStation(6);
        Check(vm.Pickups.Count==0&&vm.IsChoosingStation,"Invalid text cannot silently load the previous amount");
        amount.Text=vm.Consignments.Single(c=>c.JobId==first).AvailableHere.ToString();amount.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();ClickStation(6);
        Check(!vm.IsChoosingStation&&vm.Pickups.Count==1,"Full pickup chooses station without quantity arithmetic");
        var remove=Descendants<Button>((ItemsControl)control.FindName("PickupPlan")).Single();remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(vm.RequestedKilograms==0,"Remove undoes only the proposed pickup");
        foreach(var id in vm.Consignments.Select(c=>c.JobId).ToArray()){ClickRow(id,"Load");ClickStation(6);}
        Check(vm.Pickups.Count==2&&vm.RequestedKilograms==35,"Two consignment station choices are previewed independently");
        Save(host,Path.Combine(folder,"cargo-click-to-load-plan.png"));

        await Click("Confirm loading / unloading plan");Check(vm.ManifestText.Contains("PROPOSED / PENDING"),"Pending printout is distinct from committed cargo: "+vm.Status);
        Check(physical.Weights[5]==33,"Preview does not write the simulator");Check(!cargoGrid.IsEnabled,"Pending plan locks editable consignment controls");Check(Descendants<Expander>(control).All(e=>!e.IsExpanded),"Station and recovery details are collapsed in the simple flow");Save(host,Path.Combine(folder,"cargo-dispatch-proposed.png"));
        await Click("Apply cargo in cockpit");Check(physical.Weights[5]==68&&service.Manifest!.Cargo.Length==2,"Real bound apply command confirms 35 kg: "+vm.Status);
        foreach(var item in vm.Consignments)
        {
            var row=(DataGridRow)cargoGrid.ItemContainerGenerator.ContainerFromItem(item);
            var destination=Descendants<TextBlock>(row).Single(t=>t.Text=="TO: "+item.Destination);
            var bounds=destination.TransformToAncestor(row).TransformBounds(new Rect(0,0,destination.ActualWidth,destination.ActualHeight));
            Check(bounds.Bottom<=row.ActualHeight,"Cargo destination text is not clipped by inherited global row height");
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
        var a=vm.Consignments.Single(x=>x.Status=="DELIVER HERE");ClickRow(a.JobId,"Deliver");
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
