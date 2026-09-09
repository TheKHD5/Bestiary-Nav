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

# The installer rejects oversized previews and non-square icons.
$taskImages = @('assets/icon.png', 'assets/screenshots/uncaptured-overview.png', 'assets/screenshots/uncaptured-detail.png')
$taskImageUrls = @($taskManifest.IconUrl) + @($taskManifest.ImageUrls)
if ($taskImageUrls.Count -ne $taskImages.Count) { throw 'Expected an icon and two preview URLs.' }
for ($taskImageIndex = 0; $taskImageIndex -lt $taskImages.Count; $taskImageIndex++) {
    $taskImagePath = $taskImages[$taskImageIndex]
    if ($taskImageUrls[$taskImageIndex] -ne "https://raw.githubusercontent.com/TheKHD5/Bestiary-Nav/$taskTag/$taskImagePath") {
        throw "Incorrect versioned image URL: $taskImagePath"
    }
    $taskImageBytes = [IO.File]::ReadAllBytes((Join-Path $taskRepoRoot $taskImagePath))
    if ($taskImageBytes.Length -lt 24 -or [Convert]::ToHexString($taskImageBytes[0..7]) -ne '89504E470D0A1A0A') {
        throw "Not a PNG: $taskImagePath"
    }
    $taskWidth = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($taskImageBytes, 16))
    $taskHeight = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($taskImageBytes, 20))
    $taskValidImage = if ($taskImageIndex -eq 0) { $taskWidth -eq $taskHeight -and $taskWidth -le 512 }
        else { $taskWidth -le 730 -and $taskHeight -le 380 }
    if (!$taskValidImage -or $taskWidth -le 0 -or $taskHeight -le 0) { throw "Invalid installer image dimensions: $taskImagePath" }
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
    foreach ($taskKey in @('InternalName', 'AssemblyVersion', 'DalamudApiLevel', 'RepoUrl', 'Description', 'Punchline', 'IconUrl')) {
        if ($taskPackedManifest[$taskKey] -ne $taskManifest[$taskKey]) { throw "ZIP manifest mismatch: $taskKey" }
    }
    if (($taskPackedManifest.ImageUrls | ConvertTo-Json -Compress) -ne ($taskManifest.ImageUrls | ConvertTo-Json -Compress)) {
        throw 'ZIP preview URLs differ from the built manifest.'
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
    'RepoUrl', 'Tags', 'DalamudApiLevel', 'LoadRequiredState', 'LoadSync', 'CanUnloadAsync', 'LoadPriority', 'Punchline', 'AcceptsFeedback', 'IconUrl', 'ImageUrls')) {
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
