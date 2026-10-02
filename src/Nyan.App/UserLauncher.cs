using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nyan.App;

internal static class UserLauncher
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Startup{public int cb;public string? reserved,desktop,title;public uint x,y,xsize,ysize,xcount,ycount,fill,flags;public ushort show,reserved2;public IntPtr reservedPtr,input,output,error;}
    [StructLayout(LayoutKind.Sequential)]struct ProcessInfo{public IntPtr process,thread;public uint pid,tid;}
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr p,uint access,out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool DuplicateTokenEx(SafeAccessTokenHandle token,uint access,IntPtr security,int level,int type,out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token,uint logon,string application,StringBuilder command,uint flags,IntPtr environment,string current,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
    public static void Start(string executable,string[] arguments)
    {
        using var current=Process.GetCurrentProcess();var shells=Process.GetProcessesByName("explorer");
        try
        {
            foreach(var shell in shells)
            {
                if(shell.SessionId!=current.SessionId)continue;
                if(!OpenProcessToken(shell.Handle,2|8,out var token))continue;
                using(token)
                {
                    using var user=new WindowsIdentity(token.DangerousGetHandle());if(user.User!=WindowsIdentity.GetCurrent().User||new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))continue;
                    if(!DuplicateTokenEx(token,0x02000000,IntPtr.Zero,2,1,out var duplicate))throw new Win32Exception(Marshal.GetLastWin32Error());
                    using(duplicate)
                    {
                        var line=new StringBuilder('"'+executable+'"'+" "+string.Join(' ',arguments.Select(a=>'"'+a.Replace("\"","\\\"")+'"')));
                        var startup=new Startup{cb=Marshal.SizeOf<Startup>(),desktop=@"winsta0\default"};
                        if(!CreateProcessWithTokenW(duplicate,0,executable,line,0x08000000,IntPtr.Zero,AppContext.BaseDirectory,ref startup,out var info))throw new Win32Exception(Marshal.GetLastWin32Error());
                        CloseHandle(info.thread);CloseHandle(info.process);return;
                    }
                }
            }
            throw new InvalidOperationException("Không có shell user thường trong phiên.");
        }
        finally{foreach(var shell in shells)shell.Dispose();}
    }
}
