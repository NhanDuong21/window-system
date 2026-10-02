using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nyan.Core;

public static class Privacy
{
    public static string MaskPath(string value)
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(user) ? value : value.Replace(user, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Error(Exception error) => error is AppException ? error.Message : error switch
    {
        UnauthorizedAccessException => "Thiếu quyền truy cập; đối tượng chưa được thay đổi.",
        OperationCanceledException => "Tác vụ đã bị hủy.",
        Win32Exception w => $"Windows từ chối thao tác (mã {w.NativeErrorCode}).",
        IOException => "Không đọc/ghi được dữ liệu; file có thể đang dùng hoặc đã thay đổi.",
        _ => $"Tác vụ thất bại ({error.GetType().Name}); hãy tải lại dữ liệu."
    };
}

public static class NativeSecurity
{
    public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError=true)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr ptr);
    public static byte[] Protect(byte[] bytes) => Crypt(bytes, true);
    public static byte[] Unprotect(byte[] bytes) => Crypt(bytes, false);
    static byte[] Crypt(byte[] bytes, bool protect)
    {
        var input = new Blob { Size=bytes.Length, Data=Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            var okay = protect ? CryptProtectData(ref input,"Nyan Control Center",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output) : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!okay) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result=new byte[output.Size]; Marshal.Copy(output.Data,result,0,result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if(output.Data!=IntPtr.Zero) LocalFree(output.Data); }
    }
    public static string LocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NyanControlCenter");
    public static void CheckLocalPath(string path, bool allowMissingLeaf=false)
    {
        var full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\",StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(full)!).DriveType==DriveType.Network)
            throw new AppException("scope","Chỉ hỗ trợ đường dẫn ổ đĩa local.");
        var current=full;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current)||Directory.Exists(current))
            {
                var attrs=File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint)!=0 || ((uint)attrs & (0x1000|0x40000|0x400000))!=0)
                    throw new AppException("reparse","Đường dẫn có liên kết hoặc file cloud-only; thao tác bị chặn.");
            }
            else if (current==full && !allowMissingLeaf) throw new AppException("missing","Đối tượng không còn tồn tại; hãy tải lại.");
            current=Path.GetDirectoryName(current);
        }
    }
    public static bool IsWithin(string candidate,string root) => Path.GetFullPath(candidate).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessCritical(IntPtr process,out bool critical);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out SafeAccessTokenHandle token);
    public static Process CheckedProcess(int pid,long startTicks)
    {
        if(pid<=4||pid==Environment.ProcessId) throw new AppException("protected","Process hệ thống hoặc ứng dụng này được bảo vệ.");
        var p=Process.GetProcessById(pid);
        try
        {
            if(p.StartTime.ToUniversalTime().Ticks!=startTicks) throw new AppException("stale","PID đã thuộc process khác; hãy tải lại và tạo preview mới.");
            var protectedNames=new[]{"csrss","wininit","winlogon","lsass","services","svchost","smss","System","Registry","Memory Compression","MsMpEng","SecurityHealthService","dwm","explorer"};
            if(protectedNames.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase)||p.SessionId!=Process.GetCurrentProcess().SessionId) throw new AppException("protected","Process này được bảo vệ hoặc thuộc phiên khác.");
            if(!IsProcessCritical(p.Handle,out var critical)||critical) throw new AppException("protected","Không xác minh được process không quan trọng; thao tác bị chặn.");
            if(!OpenProcessToken(p.Handle,8,out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using(token) using(var identity=new WindowsIdentity(token.DangerousGetHandle()))
                if(identity.User!=WindowsIdentity.GetCurrent().User) throw new AppException("owner","Chỉ kết thúc process thuộc người dùng hiện tại.");
            var executable=p.MainModule?.FileName ?? throw new AppException("identity","Không đọc được danh tính executable.");
            if(IsWithin(executable,Environment.GetFolderPath(Environment.SpecialFolder.Windows))) throw new AppException("protected","Process có executable trong Windows được bảo vệ.");
            return p;
        }
        catch { p.Dispose(); throw; }
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,string l,uint flags,uint timeout,out IntPtr result);
    public static void NotifyEnvironment() => SendMessageTimeout(new IntPtr(0xffff),0x1a,IntPtr.Zero,"Environment",2,1000,out _);
}

public sealed class FileLease : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct FileInfoNative { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write; public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle h,out FileInfoNative info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle h,StringBuilder path,uint count,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFileInformationByHandle(SafeFileHandle h,int kind,IntPtr buffer,uint size);
    public SafeFileHandle Handle {get;}
    public string Identity {get;}
    public long Length {get;}
    public long WriteTicks {get;}
    public string Path {get; private set;}
    public FileLease(string path,bool mutate=false)
    {
        NativeSecurity.CheckLocalPath(path); Path=System.IO.Path.GetFullPath(path);
        Handle=CreateFile(Path,mutate ? 0x10081u : 0x80u,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        if(Handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var finalPath=new StringBuilder(32768);var finalSize=GetFinalPathNameByHandle(Handle,finalPath,32768,0);var final=finalPath.ToString();if(final.StartsWith(@"\\?\"))final=final[4..];
        if(finalSize==0||finalSize>=32768||!final.Equals(Path,StringComparison.OrdinalIgnoreCase)){Dispose();throw new AppException("race","Đường dẫn native không còn khớp; thao tác bị chặn.");}
        if(!GetFileInformationByHandle(Handle,out var info)){Dispose();throw new Win32Exception(Marshal.GetLastWin32Error());}
        if((info.Attributes&(0x400|0x1000|0x40000|0x400000|0x10))!=0){Dispose();throw new AppException("attributes","File liên kết/cloud/thư mục không được phép thay đổi.");}
        Identity=$"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}"; Length=((long)info.SizeHigh<<32)|info.SizeLow;
        WriteTicks=DateTime.FromFileTimeUtc(((long)info.Write.dwHighDateTime<<32)|(uint)info.Write.dwLowDateTime).Ticks;
        if(info.Links>1&&mutate){Dispose();throw new AppException("hardlink","File có hard link; cleanup/startup sẽ bỏ qua để bảo vệ dữ liệu.");}
    }
    public void RenameTo(string destination)
    {
        using var parent=new DirectoryLease(System.IO.Path.GetDirectoryName(destination)!);
        var filename=Encoding.Unicode.GetBytes(System.IO.Path.GetFileName(destination));
        var buffer=Marshal.AllocHGlobal(20+filename.Length);
        try
        {
            for(int i=0;i<20;i++) Marshal.WriteByte(buffer,i,0);
            Marshal.WriteIntPtr(buffer,8,parent.Handle.DangerousGetHandle());
            Marshal.WriteInt32(buffer,16,filename.Length); Marshal.Copy(filename,0,buffer+20,filename.Length);
            if(!SetFileInformationByHandle(Handle,3,buffer,(uint)(20+filename.Length))) throw new Win32Exception(Marshal.GetLastWin32Error());
            Path=System.IO.Path.GetFullPath(destination);
        } finally {Marshal.FreeHGlobal(buffer);}
    }
    public void Delete()
    {
        var buffer=Marshal.AllocHGlobal(4);
        try{Marshal.WriteInt32(buffer,1);if(!SetFileInformationByHandle(Handle,4,buffer,4))throw new Win32Exception(Marshal.GetLastWin32Error());}finally{Marshal.FreeHGlobal(buffer);}
    }
    public void Dispose()=>Handle.Dispose();
}

public sealed class DirectoryLease : IDisposable
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle h,StringBuilder path,uint count,uint flags);
    public SafeFileHandle Handle{get;}
    public DirectoryLease(string directory)
    {
        NativeSecurity.CheckLocalPath(directory);var expected=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        Handle=CreateFile(expected,0x80,1,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(Handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer=new StringBuilder(32768);var count=GetFinalPathNameByHandle(Handle,buffer,32768,0);var final=buffer.ToString();if(final.StartsWith(@"\\?\"))final=final[4..];
        if(count==0||count>=32768||!Path.TrimEndingDirectorySeparator(final).Equals(expected,StringComparison.OrdinalIgnoreCase)){Dispose();throw new AppException("race","Thư mục native không còn khớp; thao tác bị chặn.");}
    }
    public void Dispose()=>Handle.Dispose();
}
