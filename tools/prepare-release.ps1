#Requires -Version 7.2
[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRepoRoot 'BestiaryNav/BestiaryNav.csproj'
if (!$SkipBuild) {
    & $Dotnet build $taskProject -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

[xml]$taskProjectXml = Get-Content -LiteralPath $taskProject -Raw
$taskVersion = [string]$taskProjectXml.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Use a numeric release version.' }
$taskTag = "v$taskVersion"
$taskExpectedAssembly = if ($taskVersion.Split('.').Count -eq 3) { "$taskVersion.0" } else { $taskVersion }
$taskBuild = Join-Path $taskRepoRoot 'BestiaryNav/bin/Release'
$taskManifestPath = Join-Path $taskBuild 'BestiaryNav.json'
$taskDllPath = Join-Path $taskBuild 'BestiaryNav.dll'
$taskZipPath = Join-Path $taskBuild 'BestiaryNav/latest.zip'
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json -AsHashtable
$taskDllVersion = [Reflection.AssemblyName]::GetAssemblyName($taskDllPath).Version.ToString()
if ($taskManifest.InternalName -ne 'BestiaryNav' -or $taskManifest.AssemblyVersion -ne $taskExpectedAssembly -or
    $taskDllVersion -ne $taskExpectedAssembly -or $taskManifest.DalamudApiLevel -lt 1) {
    throw 'Project, DLL and manifest metadata disagree.'
}

$taskZip = [IO.Compression.ZipFile]::OpenRead($taskZipPath)
try {
    $taskAllowed = @('BestiaryNav.dll', 'BestiaryNav.json', 'BestiaryNav.deps.json')
    if ($taskZip.Entries.FullName | Where-Object { $_ -notin $taskAllowed }) { throw 'Unexpected files in install ZIP.' }
    if (@($taskZip.Entries.FullName | Select-Object -Unique).Count -ne $taskZip.Entries.Count) { throw 'Duplicate ZIP entries.' }
    $taskDllEntry = $taskZip.GetEntry('BestiaryNav.dll')
    $taskManifestEntry = $taskZip.GetEntry('BestiaryNav.json')
    if (!$taskDllEntry -or !$taskManifestEntry) { throw 'ZIP must contain the DLL and manifest at its root.' }
    $taskReader = [IO.StreamReader]::new($taskManifestEntry.Open())
    try { $taskPackedManifest = $taskReader.ReadToEnd() | ConvertFrom-Json -AsHashtable }
    finally { $taskReader.Dispose() }
    foreach ($taskKey in @('InternalName', 'AssemblyVersion', 'DalamudApiLevel', 'RepoUrl', 'Description', 'Punchline')) {
        if ($taskPackedManifest[$taskKey] -ne $taskManifest[$taskKey]) { throw "ZIP manifest mismatch: $taskKey" }
    }
    $taskDllStream = $taskDllEntry.Open()
    try { $taskPackedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskDllStream)) }
    finally { $taskDllStream.Dispose() }
    if ($taskPackedHash -ne (Get-FileHash -LiteralPath $taskDllPath -Algorithm SHA256).Hash) { throw 'ZIP contains a stale DLL.' }
}
finally { $taskZip.Dispose() }

$taskReleaseDir = Join-Path $taskRepoRoot "artifacts/$taskTag"
New-Item -ItemType Directory -Path $taskReleaseDir -Force | Out-Null
Copy-Item -LiteralPath $taskZipPath -Destination (Join-Path $taskReleaseDir 'latest.zip')
Copy-Item -LiteralPath $taskManifestPath -Destination (Join-Path $taskReleaseDir 'BestiaryNav.json')
$taskChecksums = foreach ($taskFile in @('latest.zip', 'BestiaryNav.json')) {
    $taskHash = (Get-FileHash -LiteralPath (Join-Path $taskReleaseDir $taskFile) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$taskHash  $taskFile"
}
[IO.File]::WriteAllText((Join-Path $taskReleaseDir 'checksums.txt'), ($taskChecksums -join "`n") + "`n")

$taskDownload = "https://github.com/TheKHD5/Bestiary-Nav/releases/download/$taskTag/latest.zip"
$taskEntry = [ordered]@{}
foreach ($taskKey in @('Author', 'Name', 'InternalName', 'AssemblyVersion', 'Description', 'ApplicableVersion',
    'RepoUrl', 'Tags', 'DalamudApiLevel', 'LoadRequiredState', 'LoadSync', 'CanUnloadAsync', 'LoadPriority', 'Punchline', 'AcceptsFeedback')) {
    if ($taskManifest.ContainsKey($taskKey)) { $taskEntry[$taskKey] = $taskManifest[$taskKey] }
}
$taskEntry.IsHide = $false
$taskEntry.IsTestingExclusive = $false
$taskEntry.DownloadLinkInstall = $taskDownload
$taskEntry.DownloadLinkUpdate = $taskDownload
$taskEntry.DownloadLinkTesting = $taskDownload
$taskEntry.LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$taskEntry.Changelog = "Release $taskVersion. See GitHub releases for details."
$taskIndexJson = ConvertTo-Json -InputObject @($taskEntry) -Depth 10
[IO.File]::WriteAllText((Join-Path $taskRepoRoot 'pluginmaster.json'), $taskIndexJson + "`n")
Write-Output "Prepared $taskTag ($taskExpectedAssembly): $taskReleaseDir"
Write-Output 'Generated pluginmaster.json. Publish the release assets before pushing the index to main.'
