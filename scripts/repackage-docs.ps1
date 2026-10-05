param([Parameter(Mandatory=$true)][string]$SourceFolder)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(git -C $repo status --porcelain --untracked-files=normal) { throw 'Commit documentation/configuration before packaging.' }
$documentationCommit=git -C $repo rev-parse HEAD
$release=Get-Content -LiteralPath "$PSScriptRoot/release.json" -Raw | ConvertFrom-Json
foreach($name in @($SourceFolder,[string]$release.folder)) {
    if($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]+$') { throw 'Only artifact folder names are accepted.' }
}
$artifactRoot=Join-Path $repo 'artifacts'
$source=Join-Path $artifactRoot $SourceFolder
$output=Join-Path $artifactRoot $release.folder
$oldArchive=$source+'.zip';$archive=$output+'.zip'
if((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $archive)) { throw 'Package identity already exists; preserve it and choose a new revision.' }
if(-not(Test-Path -LiteralPath $oldArchive -PathType Leaf)) { throw 'Original archive required for preservation check.' }
$original=Get-Content -LiteralPath (Join-Path $source 'build-manifest.json') -Raw | ConvertFrom-Json
if($original.version -ne $release.version -or $original.rid -ne $release.rid -or $release.packageId -ne $release.folder -or $original.entry -ne 'NyanControlCenter.exe') { throw 'Documentation-only release identity mismatch.' }
if((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source artifact must be a physical directory.' }
$oldArchiveHash=(Get-FileHash -LiteralPath $oldArchive -Algorithm SHA256).Hash
$originalHashes=@{};$binaryCount=0
foreach($file in $original.files) {
    $relative=[string]$file.path
    $path=[IO.Path]::GetFullPath((Join-Path $source $relative))
    if(-not $path.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or $relative -eq 'build-manifest.json' -or $originalHashes.ContainsKey($relative)) { throw 'Invalid or duplicate manifest path.' }
    if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source payload cannot contain reparse files.' }
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if($hash -ne $file.sha256) { throw 'Original payload checksum mismatch.' }
    $originalHashes[$relative]=$hash
    if([IO.Path]::GetExtension($relative) -in @('.exe','.dll')) { $binaryCount++ }
}
if($originalHashes['NyanControlCenter.exe'] -ne $original.sha256 -or -not $originalHashes.ContainsKey('HUONG-DAN.md')) { throw 'Original entry/guide missing or mismatched.' }
$guide=Join-Path $repo 'docs/OPERATIONS.md'
$guideText=Get-Content -LiteralPath $guide -Raw -Encoding UTF8
if(-not $guideText.Contains([string]$release.packageId) -or $guideText.Contains('NyanControlCenter-1.0.2-win-x64')) { throw 'Guide must identify the current package and contain no obsolete opening path.' }

$evidence=Join-Path $repo ('.evidence/repackage-docs-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
Set-Content -LiteralPath (Join-Path $evidence '.nyan-owned') -Value 'Owned documentation-only packaging evidence'
New-Item -ItemType Directory -Path $output | Out-Null
foreach($file in $original.files) {
    $destination=[IO.Path]::GetFullPath((Join-Path $output $file.path))
    if(-not $destination.StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Destination outside new package.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $file.path) -Destination $destination
}
Copy-Item -LiteralPath $guide -Destination (Join-Path $output 'HUONG-DAN.md') -Force
$files=@(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($output.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
if($files.Count -ne $originalHashes.Count) { throw 'Payload file set changed.' }
$comparison=@(foreach($file in $files) {
    if(-not $originalHashes.ContainsKey($file.path)) { throw 'Unexpected payload file.' }
    $same=$file.sha256 -eq $originalHashes[$file.path]
    if(-not $same -and $file.path -ne 'HUONG-DAN.md') { throw 'Documentation-only package changed executable/runtime payload.' }
    [ordered]@{path=$file.path;before=$originalHashes[$file.path];after=$file.sha256;identical=$same}
})
$manifest=[ordered]@{
    product=$original.product;version=$original.version;packageId=$release.packageId
    sourceCommit=$original.sourceCommit;binarySourceCommit=$original.sourceCommit;documentationCommit=$documentationCommit
    documentationOnly=$true;repackagedFrom=$SourceFolder;originalZipSha256=$oldArchiveHash
    rid=$original.rid;selfContained=$original.selfContained;signed=$original.signed;sha256=$original.sha256
    builtAt=$original.builtAt;packagedAt=(Get-Date).ToUniversalTime().ToString('o');entry=$original.entry;files=$files
}
$manifestPath=Join-Path $output 'build-manifest.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Compress-Archive -Path "$output/*" -DestinationPath $archive

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
$expected=@{};foreach($file in $files){$expected[$file.path]=$file.sha256}
$expected['build-manifest.json']=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$seen=@{}
try {
    foreach($entry in $zip.Entries) {
        if($entry.FullName.EndsWith('/')) { continue }
        $relative=$entry.FullName.Replace('/','\')
        if(-not $expected.ContainsKey($relative) -or $seen.ContainsKey($relative)) { throw 'Archive contains an unexpected/duplicate entry.' }
        $stream=$entry.Open();$algorithm=[Security.Cryptography.SHA256]::Create()
        try {$hash=[BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','')} finally {$algorithm.Dispose();$stream.Dispose()}
        if($hash -ne $expected[$relative]) { throw 'Archive entry checksum mismatch.' }
        $seen[$relative]=$true
    }
    if($seen.Count -ne $expected.Count) { throw 'Archive is missing payload or manifest.' }
} finally {$zip.Dispose()}
foreach($file in $original.files) {
    if((Get-FileHash -LiteralPath (Join-Path $source $file.path) -Algorithm SHA256).Hash -ne $originalHashes[$file.path]) { throw 'Original folder changed.' }
}
if((Get-FileHash -LiteralPath $oldArchive -Algorithm SHA256).Hash -ne $oldArchiveHash) { throw 'Original archive changed.' }
$archiveHash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
$comparison | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'payload-comparison.json') -Encoding utf8
[ordered]@{result='PASS';packageId=$release.packageId;version=$release.version;binarySourceCommit=$original.sourceCommit;documentationCommit=$documentationCommit;rebuilt=$false;payloadFiles=$files.Count;identicalExecutableDllFiles=$binaryCount;changedPayload=@($comparison | Where-Object {-not $_.identical} | ForEach-Object {$_.path});zipEntriesVerified=$seen.Count;exeSha256=$original.sha256;zipSha256=$archiveHash;originalZipSha256=$oldArchiveHash;originalPreserved=$true} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
Write-Host "Documentation-only package: $output"
Write-Host "EXE SHA256 unchanged: $($original.sha256)"
Write-Host "ZIP SHA256: $archiveHash"
Write-Host "All $binaryCount EXE/DLL identical; evidence: $evidence"
