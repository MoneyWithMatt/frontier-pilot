using System.Text;
using FrontierAirportBuilder.MsfsSdk;
namespace FrontierAirportBuilder.Workflow;
public static class DiagnosticLog
{
    public static async Task SaveNewAsync(string path, string contents)
    {
        CommunityInstaller.SafePath(path);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true);
        await file.WriteAsync(Encoding.UTF8.GetBytes(contents));
    }
}
