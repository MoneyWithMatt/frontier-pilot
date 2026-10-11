using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;

internal static class MicrosoftSdkInstaller
{
    // Read MSI identity without running any installer actions, including for cached downloads from earlier app versions.
    public static DependencyDownload Identify(string path)
    {
        if(MsiOpenDatabase(path,IntPtr.Zero,out var database)!=0)throw new IOException("The downloaded MSI cannot be read.");
        try
        {
            string Read(string property)
            {
                if(MsiDatabaseOpenView(database,$"SELECT `Value` FROM `Property` WHERE `Property` = '{property}'",out var view)!=0)
                    throw new IOException("The MSI product information is unavailable.");
                try
                {
                    if(MsiViewExecute(view,0)!=0||MsiViewFetch(view,out var record)!=0)throw new IOException("The MSI product information is missing.");
                    try{var buffer=new StringBuilder(1024);uint size=1024;if(MsiRecordGetString(record,1,buffer,ref size)!=0)throw new IOException("Invalid MSI product information.");return buffer.ToString();}
                    finally{MsiCloseHandle(record);}
                }
                finally{MsiCloseHandle(view);}
            }
            if(Read("ProductVersion")!=DependencySetup.RequiredVersion)throw new IOException("This installer version is not supported.");
            return Read("ProductName") switch
            {
                "Microsoft Flight Simulator 2024 SDK 1.2.4 (Core)"=>DependencySetup.Core,
                "Microsoft Flight Simulator 2024 SDK 1.2.4 (Samples)"=>DependencySetup.Samples,
                _=>throw new IOException("This is not the supported Microsoft SDK installer.")
            };
        }
        finally{MsiCloseHandle(database);}
    }
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiOpenDatabaseW")]private static extern uint MsiOpenDatabase(string path,IntPtr persist,out uint database);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiDatabaseOpenViewW")]private static extern uint MsiDatabaseOpenView(uint database,string query,out uint view);
    [DllImport("msi.dll")]private static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")]private static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiRecordGetStringW")]private static extern uint MsiRecordGetString(uint record,uint field,StringBuilder value,ref uint size);
    [DllImport("msi.dll")]private static extern uint MsiCloseHandle(uint handle);
}
