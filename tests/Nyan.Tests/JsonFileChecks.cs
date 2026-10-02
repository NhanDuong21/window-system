using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Nyan.Core;

namespace Nyan.Tests;

static class JsonFileChecks
{
    internal static async Task<List<string>> RunAsync(string evidence)
    {
        var root=Path.Combine(evidence,"json-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var script=Path.Combine(root,"write-ps51.ps1");
        File.WriteAllText(script,"""
            param([string]$Root)
            $ErrorActionPreference='Stop'
            @{product='Nyan acceptance';id='0123456789abcdef0123456789abcdef';root=$Root;value='Tiếng Việt có dấu'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Root 'ownership.json') -Encoding utf8
            @{name='NYAN_ACCEPTANCE_0123456789abcdef0123456789abcdef';start=3;files=@(@{path='NyanControlCenter.exe';sha256='fixture'})} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Root 'service-ownership.json') -Encoding utf8
            """,new UTF8Encoding(true));
        var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe");
        using var process=Process.Start(new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"-NoProfile","-File",script,"-Root",root}})!;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(false);throw new AppException("json-test","PowerShell JSON writer quá thời gian; chỉ đóng writer do test tạo.");}
        if(process.ExitCode!=0)throw new AppException("json-test","PowerShell5.1 writer thất bại.");
        var results=new List<string>();
        void Check(string title,bool ok)=>results.Add((ok?"PASS ":"FAIL ")+title);
        void Reject(string title,Action read){try{read();Check(title,false);}catch(Exception error)when(error is JsonException or AppException){Check(title,true);}}
        var owner=Path.Combine(root,"ownership.json");var raw=NativeSecurity.ReadBounded(owner,64*1024);
        Check("JSON actual Windows PowerShell5.1 output has UTF8 BOM",raw.Length>3&&raw[0]==0xef&&raw[1]==0xbb&&raw[2]==0xbf);
        Reject("JSON reproduce 1.0.1 direct-byte parser failure",()=>JsonSerializer.Deserialize<Dictionary<string,string>>(raw));
        var parsed=NativeSecurity.ReadJson<Dictionary<string,string>>(owner,64*1024);
        Check("JSON BOM manifest preserves ownership and Vietnamese text",parsed["root"]==root&&parsed["id"]=="0123456789abcdef0123456789abcdef"&&parsed["value"]=="Tiếng Việt có dấu");
        var service=NativeSecurity.ReadJson<Dictionary<string,JsonElement>>(Path.Combine(root,"service-ownership.json"),64*1024);
        Check("JSON BOM service manifest preserves numeric and array fields",service["start"].GetInt32()==3&&service["files"].GetArrayLength()==1);
        var plain=Path.Combine(root,"no-bom.json");var value="Tiếng Việt \ufeff trong giá trị";File.WriteAllText(plain,JsonSerializer.Serialize(new Dictionary<string,string>{{"value",value}}),new UTF8Encoding(false));
        Check("JSON non-BOM accepted without stripping character inside value",NativeSecurity.ReadJson<Dictionary<string,string>>(plain,64*1024)["value"]==value);
        var invalid=Path.Combine(root,"invalid.json");File.WriteAllText(invalid,"{bad}",new UTF8Encoding(true));Reject("JSON BOM does not forgive malformed payload",()=>NativeSecurity.ReadJson<object>(invalid,64*1024));
        Reject("JSON bounded native reader rejects oversized wire bytes",()=>NativeSecurity.ReadJson<object>(owner,raw.Length-1));
        var duplicate=Path.Combine(root,"duplicate-bom.json");File.WriteAllBytes(duplicate,new byte[]{0xef,0xbb,0xbf}.Concat(raw).ToArray());Reject("JSON only one leading encoding marker accepted",()=>NativeSecurity.ReadJson<object>(duplicate,64*1024));
        var utf16=Path.Combine(root,"utf16.json");File.WriteAllText(utf16,"{}",Encoding.Unicode);Reject("JSON rejects unsupported UTF16 instead of guessing encoding",()=>NativeSecurity.ReadJson<object>(utf16,64*1024));
        return results;
    }
}
