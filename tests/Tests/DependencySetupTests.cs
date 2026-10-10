using System.IO.Compression;
using System.Net;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Tests;

public sealed class DependencySetupTests : TemporaryDirectoryTest
{
    [Fact] public void MissingSdkHasActionableGuidanceAndCommunityIsDetected()
    {
        var packages=Path.Combine(Root,"Packages");Directory.CreateDirectory(Path.Combine(packages,"Community2024"));
        var config=Path.Combine(Root,"UserCfg.opt");File.WriteAllText(config,$"InstalledPackagesPath \"{packages}\"");
        var status=DependencySetup.Detect(["",Path.Combine(Root,"absent")],config);
        Assert.False(status.SdkReady);Assert.Contains("Install Core and Samples",status.SdkMessage);
        Assert.Equal(Path.Combine(packages,"Community2024"),status.CommunityPath);
        Assert.Contains("detected",status.CommunityMessage);
        Assert.Contains("Dependency setup",Assert.Throws<IOException>(()=>SdkInspector.Inspect("")).Message);
    }
    [Fact] public void AmbiguousConfigurationIsRefusedWithoutCreatingFolders()
    {
        var config=Path.Combine(Root,"UserCfg.opt");File.WriteAllText(config,"InstalledPackagesPath \"C:\\one\"\nInstalledPackagesPath \"C:\\two\"");
        var status=DependencySetup.Detect([],config);
        Assert.Equal("",status.CommunityPath);Assert.Contains("exactly one",status.CommunityMessage);
    }
    [Fact] public void CoreOnlyInstallationKeepsDiscoveredFolderAndExplainsMissingSamples()
    {
        var sdk=Path.Combine(Root,"SDK");Directory.CreateDirectory(Path.Combine(sdk,"Tools","bin"));
        File.WriteAllText(Path.Combine(sdk,"version.txt"),"1.2.4");
        File.WriteAllText(Path.Combine(sdk,"Tools","bin","fspackagetool.exe"),"fixture only");
        var status=DependencySetup.Detect([sdk,Path.Combine(Root,"missing")],Path.Combine(Root,"missing.opt"));
        Assert.False(status.SdkReady);Assert.Equal(sdk,status.SdkRoot);
        Assert.Contains("Core 1.2.4 found",status.SdkMessage);Assert.Contains("Samples is missing",status.SdkMessage);
    }
    [Theory][InlineData("1.9.0")][InlineData("1.2.4")]
    public void VersionOrUnqualifiedHashesCannotEnableSdk(string version)
    {
        var sdk=Path.Combine(Root,"SDK");Directory.CreateDirectory(Path.Combine(sdk,"Tools","bin"));
        Directory.CreateDirectory(Path.Combine(sdk,"Samples","DevmodeProjects","Sceneries","SimpleAirport"));
        Directory.CreateDirectory(Path.Combine(sdk,"SimConnect SDK","lib"));Directory.CreateDirectory(Path.Combine(sdk,"SimConnect SDK","include"));
        File.WriteAllText(Path.Combine(sdk,"version.txt"),version);File.WriteAllText(Path.Combine(sdk,"Tools","bin","fspackagetool.exe"),"fixture");
        File.WriteAllText(Path.Combine(sdk,"Samples","DevmodeProjects","Sceneries","SimpleAirport","AirportProject.xml"),"fixture");
        File.WriteAllText(Path.Combine(sdk,"SimConnect SDK","lib","SimConnect.dll"),"fixture");File.WriteAllText(Path.Combine(sdk,"SimConnect SDK","include","SimConnect.h"),"fixture");
        var status=DependencySetup.Detect([sdk],Path.Combine(Root,"missing.opt"));Assert.False(status.SdkReady);
        Assert.Contains(version=="1.2.4"?"do not match":"requires SDK 1.2.4",status.SdkMessage);
    }
    private sealed class ResponseHandler(Func<HttpResponseMessage> response):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(response());}}
    private static byte[] Archive(params string[] names)
    {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
            foreach(var name in names){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write("test fixture, not executable");}
        return memory.ToArray();
    }
    [Fact] public async Task DownloaderNeverExtractsArchivePathsOrOtherExecutables()
    {
        using var client=new HttpClient(new ResponseHandler(()=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Archive("../../escape.msi","../../cab1.cab","payload.exe"))}));
        var file=await DependencySetup.DownloadAsync(DependencySetup.Core,Root,new Progress<string>(),default,client);
        Assert.Equal("MicrosoftSdkInstaller.msi",Path.GetFileName(file));Assert.True(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(file)!,"cab1.cab")));
        Assert.False(File.Exists(Path.Combine(Root,"escape.msi")));Assert.Empty(Directory.GetFiles(Root,"*.exe",SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Root,"*.part",SearchOption.AllDirectories));
    }
    [Theory][InlineData(0)][InlineData(2)]
    public async Task MissingOrAmbiguousInstallersAreRefused(int count)
    {
        var names=count==0?new[]{"readme.txt"}:new[]{"one.msi","two.msi"};
        using var client=new HttpClient(new ResponseHandler(()=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Archive(names))}));
        await Assert.ThrowsAsync<IOException>(()=>DependencySetup.DownloadAsync(DependencySetup.Core,Root,new Progress<string>(),default,client));
        Assert.Empty(Directory.GetFiles(Root,"*.part",SearchOption.AllDirectories));
    }
    [Fact] public async Task CancellationLeavesNoPartialInstaller()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();
        using var client=new HttpClient(new ResponseHandler(()=>new(HttpStatusCode.OK)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>DependencySetup.DownloadAsync(DependencySetup.Core,Root,new Progress<string>(),stop.Token,client));
        Assert.Empty(Directory.GetFiles(Root,"*.part",SearchOption.AllDirectories));
    }
    [Fact] public async Task RedirectDoesNotBecomeExecutableDownload()
    {
        using var client=new HttpClient(new ResponseHandler(()=>new(HttpStatusCode.Redirect)));
        await Assert.ThrowsAsync<HttpRequestException>(()=>DependencySetup.DownloadAsync(DependencySetup.Core,Root,new Progress<string>(),default,client));
    }
    [Fact] public async Task ArbitraryDownloadUrlIsRejected()
    {
        var package=new DependencyDownload("Core","untrusted",new Uri("https://example.com/installer.zip"),"unknown");
        await Assert.ThrowsAsync<ArgumentException>(()=>DependencySetup.DownloadAsync(package,Root,new Progress<string>(),default));
    }
}
