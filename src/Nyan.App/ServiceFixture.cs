using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Nyan.App;

// Deliberately idle SCM fixture: no network, files, dependencies or worker tasks.
// Registration is a separate human-confirmed script; this mode cannot install services.
internal static class ServiceFixture
{
    delegate void MainCallback(uint argc,IntPtr argv);
    delegate void ControlCallback(uint control);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Entry {public string? Name;public MainCallback? Main;}
    [StructLayout(LayoutKind.Sequential)] struct Status {public uint Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool StartServiceCtrlDispatcher([In] Entry[] entries);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr RegisterServiceCtrlHandler(string name,ControlCallback callback);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetServiceStatus(IntPtr handle,ref Status status);
    static readonly ManualResetEventSlim StopEvent=new(false);static IntPtr handle;static readonly object Gate=new();
    static void Report(uint state){lock(Gate){var status=new Status{Type=0x10,State=state,Accepted=state==4?1u:0u,WaitHint=state==3?1000u:0u,Checkpoint=state==3?1u:0u};if(!SetServiceStatus(handle,ref status))StopEvent.Set();}}
    internal static int Run(string name)
    {
        if(!Regex.IsMatch(name,@"^NYAN_ACCEPTANCE_[a-f0-9]{32}$"))return 2;
        ControlCallback control=c=>{if(c==1){Report(3);StopEvent.Set();}};
        MainCallback main=(_,_)=>{handle=RegisterServiceCtrlHandler(name,control);if(handle==IntPtr.Zero)return;Report(4);StopEvent.Wait();Report(1);};
        var ok=StartServiceCtrlDispatcher([new(){Name=name,Main=main},new()]);GC.KeepAlive(main);GC.KeepAlive(control);return ok?0:1;
    }
}
