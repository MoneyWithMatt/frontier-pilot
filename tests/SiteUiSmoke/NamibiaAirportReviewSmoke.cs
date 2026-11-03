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
        var model=new BuilderViewModel(BuilderServices.CreateDryRun(),"",careerStore:store);
        var host=new MainWindow(model){Width=1280,Height=900,ShowInTaskbar=false};host.Show();await Task.Delay(400);
        await model.Career.Board.RefreshAsync();model.Career.Show(CareerSection.JobBoard);
        var hub=(CareerHubControl)host.FindName("CareerHub");
        var board=model.Career.Board;var job=board.All.First(j=>j.Family==CareerJobFamily.Cargo&&j.Destination.Contains("FYAM"));
        board.Selected=job;Pump();
        var aircraftSelector=Descendants<ComboBox>(hub).Single(c=>c.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path=="AircraftChoices");
        aircraftSelector.SelectedItem=board.AircraftChoices.Single(t=>t.Id=="c208");Pump();
        Check(board.AcceptCommand.CanExecute(null),$"Exploratory acceptance enabled for selected job {board.Selected?.SourceId}, source {model.Jobs.SelectedOffer?.JobId}, aircraft {board.SelectedAircraft?.Id}: {board.Error}");
        await board.AcceptAsync();Pump();
        Check(board.Selected!.Accepted&&board.Selected.DestinationKnowledge.Contains("Unverified"),$"Unverified Aminuis job accepts without an airport approval prompt: {board.Selected?.Status}, {board.Selected?.DestinationKnowledge}, {board.Error}");
        Check(board.Items.Where(j=>j.Family==CareerJobFamily.Cargo).Select(j=>j.DestinationLocationId).Distinct().Count()>=6,"Missionary board shows six distinct curated destinations");
        var control=(AirportReviewControl)hub.FindName("AirportReview");
        ((Expander)control.FindName("ReviewPanel")).IsExpanded=true;Pump();
        var type=Descendants<ComboBox>(control).Single(c=>c.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path=="AircraftTypes");
        type.SelectedItem=CompanyAircraft.Types.Single(t=>t.Id=="c208");
        ((TextBox)control.FindName("VisitNote")).Text="SYNTHETIC player visit; no simulator evidence";
        var verify=(Button)control.FindName("MarkVerified");verify.BringIntoView();Pump();
        Check(verify.Command.CanExecute(null),"Actual airport review command requires a named aircraft");verify.Command.Execute(null);
        while(model.Career.Airports.IsBusy)await Task.Delay(10);Pump();
        Check(model.Career.Airports.Knowledge.Contains("Verified by player"),"Actual Mark Verified control records per-aircraft player knowledge");
        Check(board.Selected!.DestinationKnowledge.Contains("Unverified for this aircraft"),"A Caravan report does not verify the selected C152");
        var confirmation=(TextBlock)control.FindName("SavedStatus");Check(confirmation.Text.Contains("visit saved"),"Airport visit confirmation is rendered after the actual save");
        Save(host,Path.Combine(folder,"namibia-airport-verified.png"));
        ((TextBox)control.FindName("VisitNote")).Text="SYNTHETIC runway issue; exercise report preservation";
        model.Career.Airports.SelectedIssue=AirportIssue.RunwayUnusable;
        var problem=(Button)control.FindName("ReportProblem");problem.BringIntoView();Pump();problem.Command.Execute(null);
        while(model.Career.Airports.IsBusy)await Task.Delay(10);Pump();
        Check(model.Career.Airports.Error==""&&model.Career.Airports.Knowledge.Contains("Problematic"),"Actual Report Problem control updates current destination knowledge");
        Check(Descendants<TextBlock>(control).Any(t=>t.Text=="Runway unusable")&&!Descendants<TextBlock>(control).Any(t=>t.Text.StartsWith("IssueOption {")),"Issue selector renders a readable category instead of a developer record");
        var confirmationPosition=confirmation.TransformToAncestor(host).Transform(new Point(0,0));
        Check(confirmationPosition.Y>=0&&confirmationPosition.Y+confirmation.ActualHeight<=host.ActualHeight,"Saved problem confirmation is brought into the current window viewport");
        Check(model.Jobs.ReservedJobs.Any(j=>j.JobId==job.SourceId)&&!model.Jobs.Offers.Any(j=>j.DestinationLocationId==job.DestinationLocationId),"Problem report withholds future offers while preserving the accepted consignment");
        Save(host,Path.Combine(folder,"namibia-airport-problem.png"));
        model.Career.Airports.SelectedAircraft=CompanyAircraft.Types.Single(t=>t.Id=="c208");
        Check(!model.Passengers.BoardCommand.CanExecute(null),"Airport player verification never grants physical boarding qualification");
        host.Close();
    }
}
