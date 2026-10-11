namespace FrontierAirportBuilder.Workflow;

public static class SdkInstallerCommand
{
    // MSI property syntax quotes the value, not the entire NAME=value token.
    // Windows Installer does not use the generic CommandLineToArgvW escaping convention.
    public static string BuildArguments(string installer,string log,string? coreRoot=null)
    {
        static string Quote(string path)
        {
            if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.IndexOfAny(['"','\r','\n','\0'])>=0)
                throw new ArgumentException("Installer paths must be absolute and cannot contain quotes or line breaks.");
            return "\""+Path.TrimEndingDirectorySeparator(path)+"\"";
        }
        return "/i "+Quote(installer)+(coreRoot==null?"":" MSFS_SDK="+Quote(coreRoot))+" /L*v "+Quote(log);
    }
}
