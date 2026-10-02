param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][ValidateSet('Register','Remove')][string]$Action)
# Human-run, outside Codex, elevated PowerShell only. Never auto-approves UAC.
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$owner=Get-Content -LiteralPath (Join-Path $Root 'ownership.json') -Raw | ConvertFrom-Json
if($owner.product -ne 'Nyan acceptance' -or $owner.root -ne $Root -or $owner.id -notmatch '^[a-f0-9]{32}$') { throw 'Invalid ownership manifest.' }
$context=Get-Content -LiteralPath (Join-Path $Root 'read-context.json') -Raw | ConvertFrom-Json
if($context.packaged -or $context.redirectedAppData -or $context.elevated) { throw 'Run ordinary-user acceptance from Explorer first. No host redirection allowed.' }
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Open elevated PowerShell yourself for this separate registration/removal step.' }
$name='NYAN_ACCEPTANCE_'+$owner.id
$base=[Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$target=[IO.Path]::GetFullPath((Join-Path $base $name))
if((Split-Path -Parent $target) -ne $base -or (Split-Path -Leaf $target) -ne $name) { throw 'Target outside owned ProgramData GUID folder.' }
$repoRoot=Split-Path -Parent $PSScriptRoot
$release=Get-Content -LiteralPath "$PSScriptRoot/release.json" -Raw | ConvertFrom-Json
$source=Join-Path $repoRoot ('artifacts/'+$release.folder)
$binary='"'+(Join-Path $target 'NyanControlCenter.exe')+'" --acceptance-service-host '+$name
$manifestPath=Join-Path $Root 'service-ownership.json'
trap {
    $failure=$_
    @{at=(Get-Date).ToUniversalTime().ToString('o');action=$Action;name=$name;folder=$target;folderPresent=(Test-Path -LiteralPath $target);error=$failure.Exception.Message;status='PARTIAL';cleanup='Preserve exact ownership manifest; no automatic stop, delete or retry'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Root ('service-'+$Action+'-failure.json')) -Encoding utf8
    throw $failure
}
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class NyanServiceFixture {
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenSCManager(string a,string b,uint rights);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateService(IntPtr scm,string name,string display,uint access,uint type,uint start,uint error,string binary,string group,IntPtr tag,string dependencies,string account,string password);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenService(IntPtr scm,string name,uint access);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool DeleteService(IntPtr service);
 [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr service);
 public static void Register(string name,string binary) {
  var scm=OpenSCManager(null,null,3);if(scm==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
  try { var s=CreateService(scm,name,name,0x10000,0x10,3,1,binary,null,IntPtr.Zero,null,@"NT AUTHORITY\LocalService",null);if(s==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());CloseServiceHandle(s); }
  finally { CloseServiceHandle(scm); }
 }
 public static void Remove(string name) {
  var scm=OpenSCManager(null,null,1);if(scm==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
  try { var s=OpenService(scm,name,0x10000);if(s==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());try { if(!DeleteService(s))throw new Win32Exception(Marshal.GetLastWin32Error()); }finally {CloseServiceHandle(s);} }
  finally { CloseServiceHandle(scm); }
 }
}
'@
function Read-OwnedService { @(Get-CimInstance -ClassName Win32_Service -Filter ("Name='"+$name+"'")) }
function Assert-FileHashes($folder,$files) {
    foreach($file in $files) {
        $path=[IO.Path]::GetFullPath((Join-Path $folder $file.path))
        if(-not $path.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected fixture file/path.' }
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Fixture file identity changed; refusing cleanup/install.' }
    }
}
if($Action -eq 'Register') {
    if((Read-OwnedService).Count -ne 0 -or (Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath $manifestPath)) { throw 'Service/folder/manifest already exists; do not reuse it.' }
    $releaseManifest=Get-Content -LiteralPath (Join-Path $source 'build-manifest.json') -Raw | ConvertFrom-Json
    if($releaseManifest.sourceCommit -ne $owner.sourceCommit -or $releaseManifest.sha256 -ne $owner.exeSha256) { throw 'Artifact does not match this acceptance session.' }
    Assert-FileHashes $source $releaseManifest.files
    Write-Host "REGISTER exact service: $name"
    Write-Host "Binary: $binary"
    Write-Host "Create folder: $target; copy portable runtime; ACL SYSTEM/Admin full, LocalService and confirming user RX. Demand-start; LocalService; no dependencies; no network; remains STOPPED."
    if((Read-Host "Type $name to confirm this registration") -cne $name) { Write-Host 'NOT_RUN'; return }
    if((Read-OwnedService).Count -ne 0 -or (Test-Path -LiteralPath $target)) { throw 'Resource appeared after confirmation.' }
    # Manifest precedes resource creation; partial failures leave exact identity and file list.
    [ordered]@{product='Nyan acceptance';id=$owner.id;name=$name;folder=$target;binary=$binary;account='NT AUTHORITY\LocalService';start=3;exeSha256=$owner.exeSha256;files=$releaseManifest.files;at=(Get-Date).ToUniversalTime().ToString('o');status='PREPARED_NOT_REGISTERED'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    New-Item -ItemType Directory -Path $target | Out-Null
    foreach($file in $releaseManifest.files) { $destination=Join-Path $target $file.path; New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null; Copy-Item -LiteralPath (Join-Path $source $file.path) -Destination $destination }
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    foreach($sid in @('S-1-5-18','S-1-5-32-544','S-1-5-19',$identity.User.Value)) {
        $rights=if($sid -in @('S-1-5-18','S-1-5-32-544')){[Security.AccessControl.FileSystemRights]::FullControl}else{[Security.AccessControl.FileSystemRights]::ReadAndExecute}
        $rule=New-Object Security.AccessControl.FileSystemAccessRule -ArgumentList @((New-Object Security.Principal.SecurityIdentifier($sid)),$rights,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $target -AclObject $acl
    Assert-FileHashes $target $releaseManifest.files
    [NyanServiceFixture]::Register($name,$binary)
    $native=Read-OwnedService
    if($native.Count -ne 1 -or $native[0].PathName -ne $binary -or $native[0].StartMode -ne 'Manual' -or $native[0].StartName -ne 'NT AUTHORITY\LocalService' -or $native[0].State -ne 'Stopped') { throw 'Native service verification failed; preserve manifest/folder and inspect exact service.' }
    $registered=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $registered.status='REGISTERED_STOPPED'
    $registered | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Host "REGISTERED_STOPPED $name. Production start/stop/restart still NOT_RUN. Use case 6 in the acceptance launcher."
} else {
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($manifest.id -ne $owner.id -or $manifest.name -ne $name -or $manifest.folder -ne $target -or $manifest.binary -ne $binary) { throw 'Ownership mismatch.' }
    $native=Read-OwnedService
    if($native.Count -gt 1 -or ($native.Count -eq 1 -and ($native[0].PathName -ne $binary -or $native[0].StartMode -ne 'Manual' -or $native[0].StartName -ne 'NT AUTHORITY\LocalService' -or $native[0].State -ne 'Stopped'))) { throw 'Native identity changed or service is running. Stop through app with confirmation; no automatic stop.' }
    if(Test-Path -LiteralPath $target) { if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse folder; refusing cleanup.' }; Assert-FileHashes $target $manifest.files }
    Write-Host "REMOVE exact STOPPED service: $name; delete only its verified GUID folder: $target"
    if((Read-Host "Type $name to confirm this separate removal") -cne $name) { Write-Host 'WAITING_FOR_USER'; return }
    $native=Read-OwnedService
    if($native.Count -eq 1) { if($native[0].PathName -ne $binary -or $native[0].State -ne 'Stopped') { throw 'Service identity/state changed after confirmation.' }; [NyanServiceFixture]::Remove($name) }
    if((Read-OwnedService).Count -ne 0) { throw 'Service deletion pending; keep exact folder and retry only after it disappears.' }
    if(Test-Path -LiteralPath $target) {
        $resolved=(Resolve-Path -LiteralPath $target).Path
        if($resolved -ne $target -or (Split-Path -Parent $resolved) -ne $base) { throw 'Resolved cleanup target outside owned ProgramData directory.' }
        Assert-FileHashes $target $manifest.files
        # Refuse extra files or any reparse node before a recursive delete, using only PowerShell end-to-end.
        $nodes=@(Get-ChildItem -LiteralPath $target -Recurse -Force)
        if($nodes | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse node; manual inspection required.' }
        if(@($nodes | Where-Object { -not $_.PSIsContainer }).Count -ne $manifest.files.Count) { throw 'Unexpected files; preserve leftovers.' }
        Remove-Item -LiteralPath $resolved -Recurse
    }
    @{at=(Get-Date).ToUniversalTime().ToString('o');name=$name;serviceAbsent=((Read-OwnedService).Count -eq 0);folderAbsent=(-not(Test-Path -LiteralPath $target))} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Root 'service-removal.json') -Encoding utf8
    Write-Host "REMOVED exact $name; evidence retained."
}
