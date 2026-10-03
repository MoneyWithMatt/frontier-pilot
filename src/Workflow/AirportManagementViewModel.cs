using System.Text.Json;
using FrontierAirportBuilder.Core;
namespace FrontierAirportBuilder.Workflow;

public sealed partial class BuilderViewModel
{
    private IAirportManagement? Management=>services as IAirportManagement;
    private string savedDefinition="",libraryWarnings="";
    private SavedAirport? selectedAirport;
    public IReadOnlyList<SavedAirport> Airports {get;private set;}=[];
    public SavedAirport? SelectedAirport {get=>selectedAirport;set{selectedAirport=value;Changed();Changed(nameof(CanOpenAirport));}}
    public bool CanOpenAirport=>!busy&&SelectedAirport!=null;
    public bool CanManageAirports=>!busy&&Management!=null;
    public bool CanEditIdentifier=>!busy&&Management?.ActiveAirportId==null;
    public bool IsEditingSavedSite=>Management?.ActiveAirportId!=null;
    public bool IsCreatingSite=>!IsEditingSavedSite;
    public string LayoutSummary=>$"{Layout.Objects.Count} buildings · {Layout.Stands.Count} parking stands · {Layout.Taxiways.Count} taxiways · {(hasRunway ? "runway placed" : "no runway")}";
    public string BuilderTitle=>IsEditingSavedSite?$"Editing saved site · {airportName} ({identifier})":"Create a new site";
    public string BuilderGuidance=>IsEditingSavedSite
        ? "Edit the restored layout below. Save site will update this same site; validate, rebuild and install to see changes in MSFS."
        : "Connect to MSFS and capture one site anchor. Place the lodge and optional airfield infrastructure on the map, then save, validate, build and install.";
    public string LibraryWarnings=>libraryWarnings;
    public string LibraryPath=>Management?.LibraryDirectory??"Airport management unavailable";
    public bool HasUnsavedAirport
    {
        get
        {
            if(Survey==null||Management==null)return false;
            try{return Layout.HasPendingProperties||JsonSerializer.Serialize(Definition())!=savedDefinition;}
            catch{return true;}
        }
    }
    public string SaveStatus=>Survey==null?"Create a survey or open a site from My Sites.":HasUnsavedAirport?"Unsaved site changes. Save the site, or build to save automatically.":"Site definition saved. Old build/install status is historical.";
    public ActionCommand SaveAirportCommand {get;private set;}=null!;
    public ActionCommand RefreshAirportsCommand {get;private set;}=null!;
    private void InitializeManagement()
    {
        SaveAirportCommand=new(()=>_=SaveAirportAsync(),()=>CanManageAirports&&Survey!=null&&!Layout.HasPendingProperties);
        RefreshAirportsCommand=new(()=>_=RefreshAirportsAsync(),()=>CanManageAirports);
    }
    private void RefreshManagement()
    {
        Changed(nameof(IsCreatingSite));Changed(nameof(LayoutSummary));
        SaveAirportCommand?.Refresh();RefreshAirportsCommand?.Refresh();Changed(nameof(HasUnsavedAirport));Changed(nameof(SaveStatus));Changed(nameof(CanOpenAirport));Changed(nameof(CanEditIdentifier));Changed(nameof(IsEditingSavedSite));Changed(nameof(BuilderTitle));Changed(nameof(BuilderGuidance));
    }
    private void SetAirports(AirportList list)
    {
        var id=selectedAirport?.Id;Airports=list.Airports;selectedAirport=Airports.FirstOrDefault(a=>a.Id==id);
        Tracking.UpdateAirports(Airports.Select(airport=>airport.Definition));
        libraryWarnings=string.Join("\n",list.Warnings);Changed(nameof(Airports));Changed(nameof(SelectedAirport));Changed(nameof(LibraryWarnings));
    }
    public Task RefreshAirportsAsync()
    {
        if(!CanManageAirports)return Task.CompletedTask;
        return Run("Read My Airports",async token=>{SetAirports(await Management!.ListAirportsAsync(token));status=$"My Airports: {Airports.Count} saved airport(s).";});
    }
    private async Task SynchronizeSavedAsync(CancellationToken token)
    {
        if(Management==null)return;
        var list=await Management.ListAirportsAsync(token);SetAirports(list);
        if(list.Airports.FirstOrDefault(a=>a.Id==Management.ActiveAirportId) is {} saved)savedDefinition=JsonSerializer.Serialize(saved.Definition);
    }
    public Task SaveAirportAsync()
    {
        if(!CanManageAirports||Survey==null)return Task.CompletedTask;
        return Run("Save site",async token=>
        {
            if(Layout.HasPendingProperties)throw new IOException("Finish/cancel drawing and apply properties before saving.");
            var definition=Definition();definition.EnsureValid();
            await Management!.SaveAirportAsync(definition,token);await SynchronizeSavedAsync(token);
            status="Site saved to My Sites. Its identifier is now fixed; use New site for a different location.";
        });
    }
    public async Task LoadAirportAsync(Guid id)
    {
        if(!CanManageAirports)return;
        SavedAirport? loaded=null;
        await Run("Load saved airport",async token=>{loaded=await Management!.LoadAirportAsync(id,token);});
        if(loaded==null)return;
        checkedReady=false;isPlacingRunway=false;Survey=loaded.Survey.Point;airportName=loaded.Name;identifier=loaded.Identifier;
        runwayTransparent=loaded.Definition.RunwayTransparent;runwayEast=loaded.Definition.RunwayEastMeters;runwayNorth=loaded.Definition.RunwayNorthMeters;runwayFirst=null;hasRunway=loaded.Definition.HasRunway;runwayHeading=loaded.Definition.TrueHeadingDegrees;Changed(nameof(HasRunway));Changed(nameof(AccessDescription));Changed(nameof(LayoutRunwayHeading));
        length=loaded.Definition.LengthMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        width=loaded.Definition.WidthMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture);surface=loaded.Definition.Surface;surfacePresetId=loaded.Definition.SurfacePresetId;Changed(nameof(SelectedSurfacePreset));Changed(nameof(SurfacePresetDetail));siteConfirmed=false;
        Layout.Reset();Layout.Replace(loaded.Definition.ParkingStands,loaded.Definition.Taxiways,loaded.Definition.PlacedObjects);Layout.MarkSaved(Layout.Stands,Layout.Objects);
        savedDefinition=JsonSerializer.Serialize(loaded.Definition);Invalidate();
        status="Saved survey and airport loaded. No live connection or previous build is assumed. Confirm the site, then validate and rebuild.";Refresh();
    }
    public void NewAirport()
    {
        if(busy)return;
        Management?.NewAirport();savedDefinition="";Survey=null;checkedReady=false;airportName="";identifier="";length="200";width="20";hasRunway=false;runwayTransparent=false;runwayHeading=0;runwayEast=0;runwayNorth=0;runwayFirst=null;isPlacingRunway=false;surface=RunwaySurface.Dirt;surfacePresetId=RunwaySurfacePresets.CompactedDirtId;Changed(nameof(HasRunway));Changed(nameof(AccessDescription));Changed(nameof(SelectedSurfacePreset));Changed(nameof(SurfacePresetDetail));Changed(nameof(IsPlacingRunway));siteConfirmed=false;
        Layout.Reset();Invalidate();status="New operation site. Check and connect, then capture its site anchor.";Refresh();
    }
    public async Task<AirportDeletionPlan?> PrepareDeleteAirportAsync(Guid id)
    {
        if(!CanManageAirports)return null;
        AirportDeletionPlan? plan=null;
        await Run("Check airport removal ownership",async token=>{plan=await Management!.PrepareDeleteAsync(id,token);status="Removal preview ready. No files have changed.";});
        return plan;
    }
    public async Task DeleteAirportAsync(AirportDeletionPlan plan,bool confirmed)
    {
        if(!CanManageAirports)return;
        bool wasActive=Management!.ActiveAirportId==plan.AirportId,deleted=false;
        string result="";
        await Run("Remove saved airport",async token=>
        {
            result=await Management.DeleteAirportAsync(plan,confirmed,token);deleted=confirmed;
            SetAirports(await Management.ListAirportsAsync(CancellationToken.None));status=result;
        });
        if(deleted&&wasActive){NewAirport();status=result;Refresh();}
    }
}
