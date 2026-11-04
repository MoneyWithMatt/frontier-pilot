using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.Desktop;

internal static partial class Program
{
    private static async Task CheckNamibiaAirportReviews(string folder)
    {
        var root=Path.Combine(Path.GetTempPath(),"FrontierNamibiaReviewUi",Guid.NewGuid().ToString("N"));
        var store=new CareerStore(root);store.SaveDraft(CareerStage.Opportunities,"Synthetic Namibia Tester","namibia");
        store.AcceptEmployment(CareerCatalogue.Employers.Single(e=>e.Category=="Missionary").Id);
        var economy=new MemoryEconomyStateStore();var catalogue=new EmployerDestinationCatalogue();
        var employer=CareerCatalogue.Employers.Single(e=>e.Category=="Missionary");
        var now=DateTimeOffset.UtcNow.AddMinutes(-10);
        var jobs=new EmployedCargoJobsService(economy,employer:employer,destinations:catalogue);
        var tracker=new FlightAttemptTracker(economy,new MemoryFlightAttemptStore()){AdditionalLocationResolver=catalogue.Find};
        var model=new BuilderViewModel(BuilderServices.CreateDryRun(),"",careerStore:store,employerDestinations:catalogue,employedCargoJobs:jobs,flightAttemptTracker:tracker);
        var host=new MainWindow(model){Width=1280,Height=900,ShowInTaskbar=false};host.Show();await Task.Delay(400);
        await model.Career.Board.RefreshAsync();model.Career.Show(CareerSection.JobBoard);
        var hub=(CareerHubControl)host.FindName("CareerHub");
        var board=model.Career.Board;var job=board.All.First(j=>j.Family==CareerJobFamily.Cargo&&j.Destination.Contains("FYAM"));
        board.Selected=job;Pump();
        var stableRows=board.All;
        for(var i=0;i<100;i++)model.CargoJourney.ClosePickup();
        await host.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.Background);
        Check(ReferenceEquals(stableRows,board.All)&&board.Selected?.Key==job.Key,"One hundred loading presentation updates preserve the bound Job Board and selected job without rebuilding collections");
        var aircraftSelector=Descendants<ComboBox>(hub).Single(c=>c.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path=="AircraftChoices");
        for(var i=0;i<20;i++)
        {
            aircraftSelector.SelectedItem=board.AircraftChoices.Single(t=>t.Id==(i%2==0?"c208":"c152"));Pump();
        }
        Check(board.Selected?.Key==job.Key&&ReferenceEquals(stableRows,board.All),"Repeated real WPF aircraft selection preserves the mission/list without recursive refresh");
        aircraftSelector.SelectedItem=board.AircraftChoices.Single(t=>t.Id=="c208");Pump();
        Check(board.AcceptCommand.CanExecute(null),$"Exploratory acceptance enabled for selected job {board.Selected?.SourceId}, source {model.Jobs.SelectedOffer?.JobId}, aircraft {board.SelectedAircraft?.Id}: {board.Error}");
        await board.AcceptAsync();Pump();
        Check(board.Selected!.Accepted&&board.Selected.DestinationKnowledge.Contains("Unverified"),$"Unverified Aminuis job accepts without an airport approval prompt: {board.Selected?.Status}, {board.Selected?.DestinationKnowledge}, {board.Error}");
        var oldAvailable=board.All.Where(j=>j.Available&&j.Family==CareerJobFamily.Cargo).Select(j=>j.Key).ToHashSet();
        var refreshButton=Descendants<Button>(hub).Single(b=>ReferenceEquals(b.Command,board.RefreshCommand));
        refreshButton.Command.Execute(null);while(board.IsBusy)await Task.Delay(10);Pump();
        Check(board.Error==""&&board.All.Where(j=>j.Available&&j.Family==CareerJobFamily.Cargo).All(j=>!oldAvailable.Contains(j.Key)),"Actual Refresh work button immediately replaces available company cargo offers");
        Check(board.Selected?.Key==job.Key&&board.Selected.Accepted&&model.Jobs.ReservedJobs.Any(j=>j.JobId==job.SourceId),"Manual board refresh retains the selected accepted mission and its reservation");
        Save(host,Path.Combine(folder,"namibia-manual-refresh.png"));
        await model.Career.OpenLoadAsync(board.Selected);Pump();
        var cargoView=Descendants<CargoJourneyControl>(hub).Single();var fuelPanel=(AircraftFuelControl)cargoView.FindName("FuelPanel");
        Check(fuelPanel.IsVisible&&ReferenceEquals(fuelPanel.DataContext,model.AircraftLoading),"Fuel panel beneath loading stations shares the existing aircraft loading service");
        var syntheticTank=new FuelTankViewModel{Id="synthetic-fuel",Name="SYNTHETIC fuel tank",CurrentGallons=12,CapacityGallons=30,PlannedGallons=12};
        model.AircraftLoading.FuelTanks.Add(syntheticTank);Pump();
        var fuelScroll=(ScrollViewer)cargoView.FindName("CargoWorkflowScroll");
        Check(fuelScroll.ViewportHeight>=80,$"Loading workspace retains a usable viewport at the ordinary window size: {fuelScroll.ViewportHeight:0} pixels");
        host.Height=1200;Pump();fuelPanel.BringIntoView();Pump();
        var fuelInput=Descendants<TextBox>(fuelPanel).Single(t=>t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="PlannedGallonsText");
        fuelInput.Text="15.5";Pump();Check(syntheticTank.PlannedGallons==15.5m,"Inline fuel input writes its declared gallon plan to the shared fuel model");
        fuelInput.Focus();fuelInput.BringIntoView();Pump();var fuelPosition=fuelInput.TransformToAncestor(host).Transform(new Point(0,0));
        Check(fuelPosition.Y>=0&&fuelPosition.Y+fuelInput.ActualHeight<=host.ActualHeight,"Inline fuel input can be brought into the visible window viewport");
        Check(!((Button)fuelPanel.FindName("ApplyFuel")).IsEnabled,"Unqualified/disconnected fuel controls cannot write simulator fuel");
        Save(host,Path.Combine(folder,"career-inline-fuel.png"));model.AircraftLoading.FuelTanks.Remove(syntheticTank);host.Height=900;model.Career.Show(CareerSection.JobBoard);Pump();
        Check(board.Items.Where(j=>j.Family==CareerJobFamily.Cargo).Select(j=>j.DestinationLocationId).Distinct().Count()>=6,"Missionary board shows six distinct curated destinations");
        Check(!model.Career.AirportArrivals.HasPrompt&&!model.Career.Airports.VerifyCommand.CanExecute(null),"No verification prompt or verification prerequisite before mission completion");
        var cargo=model.Jobs.ReservedJobs.Single(j=>j.JobId==job.SourceId);
        await tracker.SelectJobsAsync([job.SourceId]);
        AircraftTelemetry Frame(int second,bool destination,bool ground)=>new(now.AddSeconds(second),"SYNTHETIC Caravan","C208",
            (destination?cargo.DestinationLatitude:cargo.OriginLatitude)!.Value,(destination?cargo.DestinationLongitude:cargo.OriginLongitude)!.Value,ground?500:2000,0,ground?0:100,ground);
        for(var t=0;t<=10;t++)await tracker.ObserveAsync(Frame(t,false,true));
        await tracker.LoadCargoAsync();
        for(var t=11;t<=131;t++)await tracker.ObserveAsync(Frame(t,t>11,false));
        for(var t=132;t<=143;t++)await tracker.ObserveAsync(Frame(t,true,true));
        Check(tracker.Current.CanUnloadCargo,"Synthetic tracker qualifies arrival; this is not live MSFS evidence");
        await model.Jobs.RefreshLiveAsync();Check(!model.Career.AirportArrivals.HasPrompt,"Arrival proof without completed delivery/payment does not prompt");
        await tracker.UnloadCargoAsync();await model.Jobs.RefreshLiveAsync();Pump();
        var paid=(await economy.LoadAsync())!;Check(paid.FinancialLedger.Count==1&&model.Career.AirportArrivals.HasPrompt,"Optional prompt follows committed delivery and hourly wage settlement");
        var arrival=(AirportCompletionReviewControl)hub.FindName("AirportArrivalReview");
        Check(((Button)arrival.FindName("VerifyArrival")).IsEnabled&&((Button)arrival.FindName("SkipArrival")).IsEnabled,"Optional arrival actions render enabled after settlement");
        Save(host,Path.Combine(folder,"namibia-airport-first-visit.png"));
        ((Button)arrival.FindName("VerifyArrival")).Command.Execute(null);
        while(model.Career.AirportArrivals.HasPrompt&&model.Career.AirportArrivals.Error=="")await Task.Delay(10);Pump();
        Check(!model.Career.AirportArrivals.HasPrompt&&model.Career.AirportArrivals.Error=="","Actual Yes — Verify airport action closes the optional prompt");
        var control=(AirportReviewControl)hub.FindName("AirportReview");
        ((Expander)control.FindName("ReviewPanel")).IsExpanded=true;Pump();
        var type=Descendants<ComboBox>(control).Single(c=>c.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path=="AircraftTypes");
        type.SelectedItem=CompanyAircraft.Types.Single(t=>t.Id=="c208");
        ((TextBox)control.FindName("VisitNote")).Text="SYNTHETIC player visit; no simulator evidence";
        var verify=(Button)control.FindName("MarkVerified");verify.BringIntoView();Pump();
        Check(verify.Command.CanExecute(null),"Manual verification is available only after completed/settled work");verify.Command.Execute(null);
        while(model.Career.Airports.IsBusy)await Task.Delay(10);Pump();
        Check(model.Career.Airports.Knowledge.Contains("Verified by player"),"Player verification applies to the airport as a whole");
        Check(board.Selected!.DestinationKnowledge.Contains("Verified by player"),"Aircraft context cannot scope airport eligibility");
        var confirmation=(TextBlock)control.FindName("SavedStatus");Check(confirmation.Text.Contains("already verified"),"Retry does not append duplicate verification records");
        Save(host,Path.Combine(folder,"namibia-airport-verified.png"));
        ((TextBox)control.FindName("VisitNote")).Text="SYNTHETIC runway issue; exercise report preservation";
        model.Career.Airports.SelectedIssue=AirportIssue.RunwayUnusable;
        var problem=(Button)control.FindName("ReportProblem");problem.BringIntoView();Pump();problem.Command.Execute(null);
        while(model.Career.Airports.IsBusy)await Task.Delay(10);Pump();
        Check(model.Career.Airports.Error==""&&model.Career.Airports.Knowledge.Contains("Problematic"),"Actual Report Problem control updates current destination knowledge");
        Check(Descendants<TextBlock>(control).Any(t=>t.Text=="Runway / landing area missing or broken")&&!Descendants<TextBlock>(control).Any(t=>t.Text.StartsWith("IssueOption {")),"Issue categories describe simulator representation, not pilot performance decisions");
        var confirmationPosition=confirmation.TransformToAncestor(host).Transform(new Point(0,0));
        Check(confirmationPosition.Y>=0&&confirmationPosition.Y+confirmation.ActualHeight<=host.ActualHeight,"Saved problem confirmation is brought into the current window viewport");
        Check(model.Jobs.CompletedJobs.Any(j=>j.JobId==job.SourceId)&&!model.Jobs.Offers.Any(j=>j.DestinationLocationId==job.DestinationLocationId),"Problem report withholds future offers while preserving completed history");
        Save(host,Path.Combine(folder,"namibia-airport-problem.png"));
        model.Career.Airports.SelectedAircraft=CompanyAircraft.Types.Single(t=>t.Id=="c208");
        Check(!model.Passengers.BoardCommand.CanExecute(null),"Airport player verification never grants physical boarding qualification");
        // Exercise the real Skip control with a fresh isolated observation store and the same settled world.
        var skipped=new AirportCompletionReviewViewModel(new(),new MemoryAirportVisitStore(),model.Fleet,()=>Task.CompletedTask,EmployedCargoPrototype.PlayerId);
        skipped.Observe(paid);arrival.DataContext=skipped;Pump();
        ((Button)arrival.FindName("SkipArrival")).Command.Execute(null);
        while(skipped.HasPrompt&&skipped.Error=="")await Task.Delay(10);Pump();
        Check(!skipped.HasPrompt&&skipped.Error=="","Actual Skip for now dismisses optional review");
        Check(paid.FinancialLedger.SequenceEqual((await economy.LoadAsync())!.FinancialLedger),"Verification, problem reports and skipping cannot alter the settled wage");
        Check((await economy.LoadAsync())!.CompanyOfferRefreshes.Count==1,"Internal reloads and airport review actions do not reroll the board");
        host.Close();
    }
}
