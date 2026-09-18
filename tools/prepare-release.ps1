#Requires -Version 7.2
[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRepoRoot 'BestiaryNav/BestiaryNav.csproj'
$taskBinding = Get-Content -LiteralPath (Join-Path $taskRepoRoot 'BestiaryNav/binding.json') -Raw | ConvertFrom-Json
if (![string]::IsNullOrWhiteSpace($taskBinding.CandidateGameVersion) -or
    ![string]::IsNullOrWhiteSpace($taskBinding.CandidateDalamudVersion)) {
    throw 'Compatibility candidate cannot be published. Record live acceptance and promote the audited version pair first.'
}
if (!$SkipBuild) {
    & $Dotnet build $taskProject -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

[xml]$taskProjectXml = Get-Content -LiteralPath $taskProject -Raw
$taskVersion = [string]$taskProjectXml.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Use a numeric release version.' }
$taskTag = "v$taskVersion"
$taskChangelogPath = Join-Path $taskRepoRoot "releases/$taskTag.installer.txt"
if (!(Test-Path -LiteralPath $taskChangelogPath)) { throw "Missing short installer changelog: $taskChangelogPath" }
$taskChangelog = (Get-Content -LiteralPath $taskChangelogPath -Raw).Trim().Replace("`r`n", "`n")
$taskBulletLines = @($taskChangelog -split "`n" | Where-Object { $_.StartsWith('- ') })
if ($taskBulletLines.Count -lt 1 -or $taskBulletLines.Count -gt 7 -or
    @($taskBulletLines | Where-Object { $_.Length -gt 100 }).Count -gt 0 -or
    !$taskChangelog.EndsWith("Full details on GitHub repo:`nhttps://github.com/TheKHD5/Bestiary-Nav")) {
    throw 'Installer changelog needs 1-7 short bullets (100 characters max each), followed by the GitHub details footer.'
}
$taskImageTag = [string]$taskProjectXml.Project.PropertyGroup.InstallerImageTag
if ($taskImageTag -notmatch '^v\d+\.\d+\.\d+(\.\d+)?$') { throw 'Pin installer images to a numeric release tag.' }
$taskExpectedAssembly = if ($taskVersion.Split('.').Count -eq 3) { "$taskVersion.0" } else { $taskVersion }
$taskBuild = Join-Path $taskRepoRoot 'BestiaryNav/bin/Release'
$taskManifestPath = Join-Path $taskBuild 'BestiaryNav.json'
$taskDllPath = Join-Path $taskBuild 'BestiaryNav.dll'
$taskZipPath = Join-Path $taskBuild 'BestiaryNav/latest.zip'
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json -AsHashtable
if ([string]::IsNullOrWhiteSpace($taskManifest.Changelog) -or $taskManifest.Changelog.Trim().Replace("`r`n", "`n") -ne $taskChangelog) {
    throw 'Built manifest does not contain the current installer changelog. Rebuild before packaging.'
}
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
    if ($taskImageUrls[$taskImageIndex] -ne "https://cdn.jsdelivr.net/gh/TheKHD5/Bestiary-Nav@$taskImageTag/$taskImagePath") {
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
    foreach ($taskKey in @('Author', 'InternalName', 'AssemblyVersion', 'DalamudApiLevel', 'RepoUrl', 'Description', 'Punchline', 'IconUrl', 'Changelog')) {
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
$taskEntry.Changelog = $taskChangelog
$taskIndexJson = ConvertTo-Json -InputObject @($taskEntry) -Depth 10
[IO.File]::WriteAllText((Join-Path $taskRepoRoot 'pluginmaster.json'), $taskIndexJson + "`n")
Write-Output "Prepared $taskTag ($taskExpectedAssembly): $taskReleaseDir"
Write-Output 'Generated pluginmaster.json. Publish the release assets before pushing the index to main.'
