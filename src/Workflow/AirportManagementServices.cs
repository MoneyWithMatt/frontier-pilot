using System.Security.Cryptography;
using System.Text.Json;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
namespace FrontierAirportBuilder.Workflow;

public sealed partial class BuilderServices
{
    private SavedAirport? activeAirport;
    private AirportDeletionPlan? issuedDeletion;
    private IEconomyStateStore? economyDependencyStore;
    private AirportLibrary Library=>new(root,IsDryRun);
    public Guid? ActiveAirportId=>activeAirport?.Id;
    public string LibraryDirectory=>Library.DirectoryPath;
    internal void SetEconomyDependencyStore(IEconomyStateStore store)=>economyDependencyStore=store;
    private async Task EnsureNoEconomicSiteAsync(Guid airportId,CancellationToken token)
    {
        if(economyDependencyStore is null)return;
        var world=await economyDependencyStore.LoadAsync(token).ConfigureAwait(false);
        var site=world?.Sites.SingleOrDefault(x=>x.AirportDefinitionId==airportId.ToString("N"));
        if(site is not null)throw new InvalidOperationException($"Cannot delete this airport because economic operation site '{site.Name}' depends on it. Abandonment, relocation and demolition are not implemented.");
    }
    public static BuilderServices CreateDesktopDryRun()
    {
        string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrontierAirportBuilder","dry-run-profile.txt");
        CommunityInstaller.SafePath(profile);Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        string id;
        try
        {
            using var stream=new FileStream(profile,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            using var writer=new StreamWriter(stream);id=Guid.NewGuid().ToString("N");writer.Write(id);
        }
        catch(IOException) when(File.Exists(profile)){id=File.ReadAllText(profile).Trim();}
        if(!Guid.TryParseExact(id,"N",out _))throw new IOException("Invalid dry-run profile. It was preserved.");
        return new(Path.Combine(Path.GetTempPath(),"FrontierAirportBuilder-DryRuns",id),null,false);
    }
    // Used for restart tests; a caller cannot substitute a real Community destination.
    public BuilderServices ReopenDryRun()=>sandbox==null?throw new InvalidOperationException("Not a dry-run session."):new(sandbox,null,false);
    public void NewAirport(){activeAirport=null;surveyArchive=null;capturedSurvey=null;issuedBuild=null;captureEvidence="";ProjectDirectory="";EvidenceDirectory="";LogPath="";}
    public Task<AirportList> ListAirportsAsync(CancellationToken token)=>Task.Run(()=>Library.List(),token);
    public Task<SavedAirport> SaveAirportAsync(AirstripDefinition definition,CancellationToken token)=>Task.Run(()=>
    {using var operation=Library.Lock();token.ThrowIfCancellationRequested();return SaveAirportCore(definition);},token);
    private SavedAirport SaveAirportCore(AirstripDefinition definition)
    {
        if(capturedSurvey==null||surveyArchive==null)throw new IOException("Capture or load a validated survey before saving.");
        AirportLibrary.MatchSurvey(definition,capturedSurvey);
        return activeAirport=Library.Save(activeAirport?.Id,activeAirport?.Revision,definition,surveyArchive);
    }
    private void EnsureActiveRevision()
    {
        if(activeAirport==null||Library.Read(activeAirport.Id).Revision!=activeAirport.Revision)
            throw new IOException("Saved airport changed or was removed in another window. Reload it before continuing.");
    }
    private void UpdateAirport(Func<SavedAirport,SavedAirport> update)
    {
        EnsureActiveRevision();var next=update(activeAirport!);next=next with {Revision=activeAirport!.Revision+1};
        Library.Write(next);activeAirport=next;
    }
    public Task<SavedAirport> LoadAirportAsync(Guid id,CancellationToken token)=>Task.Run(()=>
    {
        using var operation=Library.Lock();var saved=Library.Read(id);token.ThrowIfCancellationRequested();
        // Revalidates saved frames; does not claim a new simulator connection or a valid old build.
        activeAirport=saved;surveyArchive=saved.Survey;capturedSurvey=saved.Survey.Point;issuedBuild=null;
        ProjectDirectory="";LogPath="";EvidenceDirectory=LibraryDirectory;captureEvidence=Library.DocumentPath(id);
        return saved;
    },token);
    private InstallationReceipt? OwnedInstallation(SavedAirport saved)
    {
        var ledger=Path.Combine(root,"ownership",CommunityInstaller.PackageName(saved.Identifier)+".json");CommunityInstaller.SafePath(ledger);
        if(File.Exists(ledger))return Installer().InspectOwnedRemoval(saved.Identifier);
        if(saved.LastInstall!="not installed")throw new IOException("Installation history exists but its ownership receipt is missing. Deletion refused; saved airport preserved.");
        if(saved.Artifacts.Count>0)
        {
            var installer=Installer();installer.ValidateDestination();
            var target=Path.Combine(installer.Community,CommunityInstaller.PackageName(saved.Identifier));CommunityInstaller.Within(target,installer.Community);
            if(Directory.Exists(target)||File.Exists(target))throw new IOException("A package exists without an ownership receipt, possibly from an interrupted install. Deletion refused; all content was preserved.");
        }
        return null;
    }
    private AirportDeletionPlan InspectDeletion(Guid id)
    {
        var saved=Library.Read(id);
        if(!IsDryRun)AssertSimulatorClosed();
        foreach(var artifact in saved.Artifacts)Library.VerifyProject(saved,artifact);
        var installed=OwnedInstallation(saved);
        var projects=saved.Artifacts.Select(a=>Library.ProjectPath(a.Key)).Where(Directory.Exists).ToArray();
        string? package=installed!=null&&Directory.Exists(installed.InstallPath)?installed.InstallPath:null;
        var summary=$"Delete {saved.Name} ({saved.Identifier})?\n\nRemove its saved definition and embedded survey, {projects.Length} verified project/build folder(s)"+
            (package!=null?$", and this installed package:\n{package}":". No verified installed package is present.")+
            $"\n\nSaved definition:\n{Library.DocumentPath(id)}\n"+
            (projects.Length>0?"\nProject/build folders:\n"+string.Join("\n",projects):"")+
            "\n\nOther airports, original survey evidence and previous-install backups will be kept. This is an administrative removal, not a gameplay action.";
        var fingerprint=Fingerprint(Library.DocumentPath(id)).Sha256+(installed!=null?Fingerprint(Installer().OwnershipFile(saved.Identifier)).Sha256:"");
        return new(id,saved.Revision,saved.Name,saved.Identifier,projects,package,fingerprint,summary);
    }
    public async Task<AirportDeletionPlan> PrepareDeleteAsync(Guid id,CancellationToken token)
    {
        await EnsureNoEconomicSiteAsync(id,token).ConfigureAwait(false);
        return await Task.Run(()=>
        {
            using var operation=Library.Lock();using var community=CommunityInstaller.LockForRemoval();
            token.ThrowIfCancellationRequested();return issuedDeletion=InspectDeletion(id);
        },token).ConfigureAwait(false);
    }
    public async Task<string> DeleteAirportAsync(AirportDeletionPlan plan,bool confirmed,CancellationToken token)
    {
        if(!confirmed)return "Deletion cancelled. No files changed.";
        await EnsureNoEconomicSiteAsync(plan.AirportId,token).ConfigureAwait(false);
        return await Task.Run(()=>
        {
        if(!ReferenceEquals(plan,issuedDeletion))throw new IOException("Request a fresh deletion preview and confirmation.");
        using var operation=Library.Lock();using var community=CommunityInstaller.LockForRemoval();
        var fresh=InspectDeletion(plan.AirportId);
        if(fresh.Revision!=plan.Revision||fresh.Fingerprint!=plan.Fingerprint||fresh.Summary!=plan.Summary)throw new IOException("Airport changed after the deletion preview. Review a new confirmation.");
        var saved=Library.Read(plan.AirportId);var installed=OwnedInstallation(saved);
        token.ThrowIfCancellationRequested();issuedDeletion=null;
        string recovery=Path.Combine(root,"removals",Guid.NewGuid().ToString("N")[..16]);CommunityInstaller.Within(recovery,Path.Combine(root,"removals"));Directory.CreateDirectory(recovery);
        File.WriteAllText(Path.Combine(recovery,"removal-plan.json"),JsonSerializer.Serialize(fresh,AirportLibrary.Json));
        // Move only verified, explicitly owned targets. Roll back ordinary failures before logical commit.
        var moved=new List<(string From,string To,bool Directory,IReadOnlyList<PackageFile> Files)>();
        void MoveDirectory(string from,string to,IReadOnlyList<PackageFile> expected)
        {
            CommunityInstaller.SafePath(from);CommunityInstaller.Within(to,recovery);
            CommunityInstaller.VerifyInventory(expected,CommunityInstaller.Inventory(from));Directory.Move(from,to);moved.Add((from,to,true,expected));
        }
        void MoveFile(string from,string name)
        {
            CommunityInstaller.SafePath(from);var to=Path.Combine(recovery,name);CommunityInstaller.Within(to,recovery);
            var file=Fingerprint(from);File.Move(from,to);moved.Add((from,to,false,[file]));
        }
        try
        {
            foreach(var artifact in saved.Artifacts)
            {
                var path=Library.ProjectPath(artifact.Key);
                if(Directory.Exists(path))MoveDirectory(path,Path.Combine(recovery,artifact.Key),artifact.Files);
            }
            if(installed!=null)
            {
                if(Directory.Exists(installed.InstallPath))MoveDirectory(installed.InstallPath,Path.Combine(recovery,"installed"),installed.Files);
                MoveFile(Installer().OwnershipFile(saved.Identifier),"ownership.json");
            }
            // Source of truth is removed last; a failure before this point restores previous locations.
            MoveFile(Library.DocumentPath(saved.Id),"airport.json");
        }
        catch
        {
            foreach(var move in moved.AsEnumerable().Reverse())
            {
                CommunityInstaller.SafePath(move.From);CommunityInstaller.Within(move.To,recovery);
                if(move.Directory)Directory.Move(move.To,move.From);else File.Move(move.To,move.From);
            }
            throw;
        }
        if(activeAirport?.Id==saved.Id)NewAirport();
        var failures=new List<string>();
        // No recursive deletion: unexpected files or redirections are preserved, including during cleanup.
        foreach(var move in moved)
        {
            try
            {
                if(move.Directory)PurgeVerified(move.To,move.Files,recovery);
                else
                {
                    CommunityInstaller.Within(move.To,recovery);var actual=Fingerprint(move.To);
                    if(actual.Bytes!=move.Files[0].Bytes||actual.Sha256!=move.Files[0].Sha256)throw new IOException("Recovery file changed.");
                    File.Delete(move.To);
                }
            }
            catch(Exception e){failures.Add(e.Message);}
        }
        File.WriteAllText(Path.Combine(recovery,"removal-report.json"),JsonSerializer.Serialize(new{saved.Id,saved.Identifier,removedUtc=DateTimeOffset.UtcNow,failures},AirportLibrary.Json));
        return failures.Count==0?$"Deleted {saved.Name} ({saved.Identifier}) and its verified active files. Previous-install backups and original survey evidence were kept.":
            $"Airport removed from My Airports and Community, but some cleanup files were preserved in {recovery}: "+string.Join("; ",failures);
        },token).ConfigureAwait(false);
    }
    private static PackageFile Fingerprint(string path){using var stream=File.OpenRead(path);return new(Path.GetFileName(path),stream.Length,Convert.ToHexString(SHA256.HashData(stream)));}
    private static void PurgeVerified(string path,IReadOnlyList<PackageFile> files,string recovery)
    {
        CommunityInstaller.Within(path,recovery);CommunityInstaller.VerifyInventory(files,CommunityInstaller.Inventory(path));
        foreach(var file in files)
        {
            var target=Path.Combine(path,file.Path);CommunityInstaller.Within(target,path);var actual=Fingerprint(target);
            if(actual.Bytes!=file.Bytes||actual.Sha256!=file.Sha256)throw new IOException("File changed during removal; it was preserved.");
            File.Delete(target);
        }
        void Empty(string dir)
        {
            CommunityInstaller.Within(dir,recovery);
            foreach(var child in Directory.EnumerateDirectories(dir)){CommunityInstaller.Within(child,path);Empty(child);}
            Directory.Delete(dir,false); // Fails, rather than deleting an unexpected remaining file.
        }
        Empty(path);
    }
}
