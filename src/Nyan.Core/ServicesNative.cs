using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Nyan.Core;

public sealed record ServiceIdentity(string Name,uint State,uint Type,uint StartType,string Binary,uint Accepted,string[] Dependents)
{
    public string Fingerprint => Privacy.Hash($"{Name}\0{State}\0{Type}\0{StartType}\0{Binary}\0{string.Join('|',Dependents)}");
}
public static class ServicesNative
{
    [StructLayout(LayoutKind.Sequential)] struct Status {public uint Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint,Pid,Flags;}
    [StructLayout(LayoutKind.Sequential)] struct Config {public uint Type,Start,Error;public IntPtr Binary,Group;public uint Tag;public IntPtr Dependencies,Account,Display;}
    [StructLayout(LayoutKind.Sequential)] struct EnumStatus {public IntPtr Name,Display; public uint Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool CloseServiceHandle(IntPtr h);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool QueryServiceStatusEx(IntPtr h,int level,out Status s,int size,out int needed);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryServiceConfig(IntPtr h,IntPtr buffer,uint size,out uint needed);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool EnumDependentServices(IntPtr h,uint state,IntPtr buffer,uint size,out uint needed,out uint returned);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool StartService(IntPtr h,uint argc,IntPtr argv);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool ControlService(IntPtr h,uint control,out Status status);
    static readonly string[] Blocked={"WinDefend","WdNisSvc","SecurityHealthService","wscsvc","RpcSs","DcomLaunch","PlugPlay","Power","EventLog","SamSs","LSM","Schedule","BFE","MpsSvc","Dhcp","Dnscache","nsi","Winmgmt","ProfSvc","UserManager","LanmanWorkstation","CryptSvc","TrustedInstaller"};
    static IntPtr Manager(){var h=OpenSCManager(null,null,1);if(h==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());return h;}
    public static ServiceIdentity Read(string name)
    {
        ValidateName(name);var manager=Manager();var service=IntPtr.Zero;
        try
        {
            service=OpenService(manager,name,1|4|8); if(service==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            if(!QueryServiceStatusEx(service,0,out var state,Marshal.SizeOf<Status>(),out _))throw new Win32Exception(Marshal.GetLastWin32Error());
            QueryServiceConfig(service,IntPtr.Zero,0,out var bytes);if(bytes==0||bytes>64*1024)throw new AppException("service","Không đọc được cấu hình service.");
            var buffer=Marshal.AllocHGlobal((int)bytes);
            Config config; string binary;
            try{if(!QueryServiceConfig(service,buffer,bytes,out _))throw new Win32Exception(Marshal.GetLastWin32Error());config=Marshal.PtrToStructure<Config>(buffer);binary=Marshal.PtrToStringUni(config.Binary)??"";}finally{Marshal.FreeHGlobal(buffer);}
            var dependents=new List<string>();
            EnumDependentServices(service,1,IntPtr.Zero,0,out var dependentBytes,out _);
            if(dependentBytes>0)
            {
                if(dependentBytes>1024*1024)throw new AppException("service","Danh sách dependency vượt giới hạn.");
                buffer=Marshal.AllocHGlobal((int)dependentBytes);
                try{if(!EnumDependentServices(service,1,buffer,dependentBytes,out _,out var count))throw new Win32Exception(Marshal.GetLastWin32Error());for(int i=0;i<count;i++){var d=Marshal.PtrToStructure<EnumStatus>(buffer+i*Marshal.SizeOf<EnumStatus>());dependents.Add(Marshal.PtrToStringUni(d.Name)??"?");}}finally{Marshal.FreeHGlobal(buffer);}
            }
            return new(name,state.State,config.Type,config.Start,binary,state.Accepted,dependents.Order().ToArray());
        }finally{if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
    public static void ValidateName(string name){if(!Regex.IsMatch(name,@"^[A-Za-z0-9_.-]{1,256}$"))throw new AppException("service-name","Tên service không hợp lệ.");}
    public static void ValidateAction(ServiceIdentity s,ActionKind action)
    {
        ValidateName(s.Name);
        var path=s.Binary.Trim();
        if(path.StartsWith('"')){var end=path.IndexOf('"',1);path=end>1?path[1..end]:"";}
        else {var exe=path.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);path=exe>=0?path[..(exe+4)]:"";if(path.Contains(' '))throw new AppException("service-path","Service có executable path không đặt trong dấu ngoặc kép; thao tác bị chặn.");}
        path=Environment.ExpandEnvironmentVariables(path);
        if(Blocked.Contains(s.Name,StringComparer.OrdinalIgnoreCase)||s.Type!=0x10||!Path.IsPathFullyQualified(path)||!File.Exists(path)||NativeSecurity.IsWithin(path,Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
            throw new AppException("service-policy","Chỉ điều khiển service ứng dụng bên thứ ba kiểu Win32OwnProcess có executable xác minh được ngoài Windows. Service này được bảo vệ.");
        NativeSecurity.CheckLocalPath(path);
        if(action is ActionKind.StopService or ActionKind.RestartService)
        {
            if(s.Dependents.Length>0)throw new AppException("dependency","Có service phụ thuộc đang chạy; không dừng dây chuyền.");
            if(s.State!=4||(s.Accepted&1)==0)throw new AppException("service-state","Service chưa ở trạng thái chạy ổn định hoặc không nhận lệnh stop.");
        }
        else if(s.State!=1||s.StartType==4)throw new AppException("service-state","Chỉ start service đang dừng và không bị Disabled.");
    }
    public static async Task PerformAsync(string name,ActionKind action,string fingerprint,CancellationToken ct)
    {
        var current=Read(name);if(current.Fingerprint!=fingerprint)throw new AppException("stale","Cấu hình/trạng thái service thay đổi; hãy tạo preview mới.");ValidateAction(current,action);
        var manager=Manager();var service=IntPtr.Zero;
        try
        {
            service=OpenService(manager,name,4|0x10|0x20);if(service==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            ct.ThrowIfCancellationRequested();
            if(action is ActionKind.StopService or ActionKind.RestartService){if(!ControlService(service,1,out _))throw new Win32Exception(Marshal.GetLastWin32Error());await WaitAsync(service,1,ct);}
            if(action is ActionKind.StartService or ActionKind.RestartService){if(!StartService(service,0,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());await WaitAsync(service,4,ct);}
        }finally{if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
    static async Task WaitAsync(IntPtr h,uint desired,CancellationToken ct)
    {
        var until=DateTime.UtcNow.AddSeconds(25);
        while(DateTime.UtcNow<until){if(!QueryServiceStatusEx(h,0,out var s,Marshal.SizeOf<Status>(),out _))throw new Win32Exception(Marshal.GetLastWin32Error());if(s.State==desired)return;await Task.Delay(250,ct);}
        throw new AppException("service-timeout","Hết thời gian chờ; service có thể đã nhận lệnh. Tải lại để kiểm tra trạng thái thực tế.");
    }
}
