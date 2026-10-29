using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.Desktop;

internal static partial class Program
{
    private static async Task CheckEmployerFleet(CareerStore career,CareerEmployer company,string folder)
    {
        var profile=career.Load();var store=new AircraftFleetStore(Path.Combine(career.StorageRoot,"fleet","aircraft-fleet.json"));var allocated=store.Load();
        Check(allocated.Aircraft.Count==company.Fleet.Length&&allocated.Aircraft.All(a=>a.OwnerEntityId==company.Id&&a.AssignedPlayerId==profile.PlayerId),"Actual employment button automatically provisions company fleet: "+company.Name);
        var vm=new AircraftFleetViewModel(store,()=>throw new Exception("Company fleet UI smoke must not connect to simulator"));vm.ConfigureEmployment(profile.PlayerId!.Value,company);
        var panel=new AircraftFleetPanel{DataContext=vm};var host=new Window{Content=panel,Width=1200,Height=900,ShowInTaskbar=false};host.Show();Pump();
        var list=Descendants<ListBox>(panel).Single();Check(list.Items.Count==company.Fleet.Length,"Fleet control shows current player's allocated aircraft");
        Check(Descendants<Expander>(panel).Single(e=>e.Header?.ToString()=="Legacy / personal registration").Visibility==Visibility.Collapsed,"Employed onboarding requires no manual registration control");
        var selected=vm.Aircraft.Last(a=>CompanyAircraft.Types.Single(t=>t.Id==a.CompanyTypeId).Enabled);list.SelectedValue=selected.Id;Pump();
        Check(vm.Selected?.Id==selected.Id&&store.Load().SelectedId==selected.Id,"Actual fleet selector persists company aircraft GUID");
        Check(vm.Selected!.Latest is null&&vm.Selected.AllocatedBase is not null,"Assigned base does not invent observed simulator placement");
        Check(vm.Overview.Contains("Company property")&&vm.Overview.Contains("Unknown"),"Ownership and unknown historical condition/hours are explicit");
        Save(host,Path.Combine(folder,"company-fleet-"+company.Category.ToLowerInvariant()+".png"));
        await vm.ShutdownAsync();host.Close();
    }
}
