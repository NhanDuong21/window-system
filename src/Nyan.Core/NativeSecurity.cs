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
    [DllImport("shell32.dll")]static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetCurrentPackageFullName(ref uint length,StringBuilder? name);
    public static bool HasPackageIdentity {get{uint length=0;return GetCurrentPackageFullName(ref length,null)!=15700;}}
    static readonly Lazy<(string Root,bool Redirected)> DataRoot=new(ResolveDataRoot);
    public static string LocalRoot=>DataRoot.Value.Root;
    public static bool HasAppDataRedirection=>DataRoot.Value.Redirected;
    static (string Root,bool Redirected) ResolveDataRoot()
    {
            // Ask Windows for its actual redirection target when a desktop host
            // has package identity. Do not weaken native final-path checks.
            var id=new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");var error=SHGetKnownFolderPath(ref id,0x40000,IntPtr.Zero,out var pointer);
            if(error!=0)Marshal.ThrowExceptionForHR(error);
            try
            {
                var root=Path.Combine(Marshal.PtrToStringUni(pointer)??throw new AppException("appdata","Không đọc được thư mục dữ liệu local."),"NyanControlCenter");
                return DirectoryLease.ResolveAppRoot(root);
            }finally{Marshal.FreeCoTaskMem(pointer);}
    }
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
    public static byte[] ReadBounded(string path,int maximum)
    {
        using var lease=new FileLease(path,readContents:true);if(lease.Length>maximum)throw new AppException("size","File vượt giới hạn an toàn.");var bytes=new byte[(int)lease.Length];int offset=0;
        while(offset<bytes.Length){int read=RandomAccess.Read(lease.Handle,bytes.AsSpan(offset),offset);if(read==0)throw new AppException("read","File không đầy đủ; thao tác bị chặn.");offset+=read;}return bytes;
    }
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
    public FileLease(string path,bool mutate=false,bool readContents=false)
    {
        NativeSecurity.CheckLocalPath(path); Path=System.IO.Path.GetFullPath(path);
        Handle=CreateFile(Path,mutate ? 0x10081u : readContents?0x81u:0x80u,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
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
        var full=System.IO.Path.GetFullPath(destination);
        var leaf=System.IO.Path.GetFileName(full);
        if(string.IsNullOrEmpty(leaf)||leaf.Contains(':'))throw new AppException("path","Tên file đích không hợp lệ hoặc là alternate data stream.");
        NativeSecurity.CheckLocalPath(full,true);
        // Keep the verified parent open without FILE_SHARE_DELETE while resolving the
        // absolute destination. This Windows build rejects a non-null RootDirectory
        // for FileRenameInfo even with a correctly terminated relative name.
        using var parent=new DirectoryLease(System.IO.Path.GetDirectoryName(full)!);
        var filename=Encoding.Unicode.GetBytes(full);
        var rootOffset=IntPtr.Size==8?8:4;
        var lengthOffset=rootOffset+IntPtr.Size;
        var nameOffset=lengthOffset+sizeof(uint);
        var bufferSize=checked(nameOffset+filename.Length+sizeof(char));
        var buffer=Marshal.AllocHGlobal(bufferSize);
        try
        {
            // ReplaceIfExists=false, RootDirectory=NULL. FileNameLength excludes
            // the UTF-16 NUL, but the Win32 path conversion requires its storage.
            for(int i=0;i<nameOffset;i++)Marshal.WriteByte(buffer,i,0);
            Marshal.WriteInt32(buffer,lengthOffset,filename.Length);
            Marshal.Copy(filename,0,buffer+nameOffset,filename.Length);
            Marshal.WriteInt16(buffer,nameOffset+filename.Length,0);
            if(!SetFileInformationByHandle(Handle,3,buffer,(uint)bufferSize))throw new Win32Exception(Marshal.GetLastWin32Error());
            Path=full;
            var finalPath=new StringBuilder(32768);var count=GetFinalPathNameByHandle(Handle,finalPath,32768,0);var final=finalPath.ToString();if(final.StartsWith(@"\\?\"))final=final[4..];
            if(count==0||count>=32768||!final.Equals(full,StringComparison.OrdinalIgnoreCase))throw new AppException("race","Đường dẫn sau đổi tên không còn khớp; dừng thao tác tiếp theo.");
            GC.KeepAlive(parent);
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
    [StructLayout(LayoutKind.Sequential)]struct DirectoryInfoNative {public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle handle,out DirectoryInfoNative info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle h,StringBuilder path,uint count,uint flags);
    public SafeFileHandle Handle{get;}
    internal static (string Root,bool Redirected) ResolveAppRoot(string expected)
    {
        // Some Desktop Bridge hosts keep the logical LocalAppData path even
        // with RETURN_FILTER_REDIRECTION_TARGET. Resolve only our fixed app
        // directory. Descendants can lack package identity while Windows still
        // redirects file writes. Detect the mapping from the acquired handle.
        NativeSecurity.CheckLocalPath(expected,true);Directory.CreateDirectory(expected);NativeSecurity.CheckLocalPath(expected);
        using var handle=CreateFile(expected,0x81,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        if(!GetFileInformationByHandle(handle,out var info))throw new Win32Exception(Marshal.GetLastWin32Error());
        if((info.Attributes&0x10)==0||(info.Attributes&(0x400|0x1000|0x40000|0x400000))!=0)throw new AppException("attributes","Thư mục dữ liệu app không an toàn.");
        var buffer=new StringBuilder(32768);var count=GetFinalPathNameByHandle(handle,buffer,32768,0);var final=buffer.ToString();if(final.StartsWith(@"\\?\"))final=final[4..];
        var packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
        var relative=Path.GetRelativePath(packages,final).Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar);
        var packageCache=System.Text.RegularExpressions.Regex.IsMatch(relative,@"^[A-Za-z0-9.-]+_[A-Za-z0-9]{13}\\LocalCache\\Local\\NyanControlCenter$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if(count==0||count>=32768||(!final.Equals(expected,StringComparison.OrdinalIgnoreCase)&&!packageCache))throw new AppException("appdata","Đích chuyển hướng không thuộc thư mục dữ liệu app được hỗ trợ.");
        NativeSecurity.CheckLocalPath(final);return (final,packageCache||!final.Equals(expected,StringComparison.OrdinalIgnoreCase));
    }
    public DirectoryLease(string directory)
    {
        NativeSecurity.CheckLocalPath(directory);var expected=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        // FILE_READ_ATTRIBUTES alone is metadata access and does not participate
        // in share/delete arbitration. Include FILE_LIST_DIRECTORY to pin the
        // verified directory against rename/deletion for this lease's lifetime.
        // Share directory reads/writes so the rename can add its target entry;
        // deliberately exclude FILE_SHARE_DELETE, which protects the parent.
        Handle=CreateFile(expected,0x81,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);if(Handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        if(!GetFileInformationByHandle(Handle,out var info)){var error=Marshal.GetLastWin32Error();Dispose();throw new Win32Exception(error);}
        if((info.Attributes&0x10)==0||(info.Attributes&(0x400|0x1000|0x40000|0x400000))!=0){Dispose();throw new AppException("attributes","Thư mục native có liên kết/cloud hoặc không phải thư mục; thao tác bị chặn.");}
        var buffer=new StringBuilder(32768);var count=GetFinalPathNameByHandle(Handle,buffer,32768,0);var final=buffer.ToString();if(final.StartsWith(@"\\?\"))final=final[4..];
        if(count==0||count>=32768||!Path.TrimEndingDirectorySeparator(final).Equals(expected,StringComparison.OrdinalIgnoreCase)){Dispose();throw new AppException("race","Thư mục native không còn khớp; thao tác bị chặn.");}
    }
    public void Dispose()=>Handle.Dispose();
}
