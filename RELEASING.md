# Publishing a release

`pluginmaster.json` is the custom repository consumed by Dalamud. It points to an immutable, version-tagged GitHub release ZIP. The SDK generates the plugin manifest inside that ZIP; this is separate from the repository index. See [Dalamud's custom repository format](https://dalamud.dev/plugin-publishing/custom-repositories/).

## Prepare

Compatibility candidates keep `candidateGameVersion` and `candidateDalamudVersion`
separate from the last verified pair in `binding.json`. The release script rejects
them. Record the required live acceptance, promote the candidate pair to the verified
fields, and remove both candidate fields before publishing. Do not promote a game
update based only on build or fixture-test success.

1. Update `BestiaryNav/BestiaryNav.csproj` with the new numeric version. Never overwrite an already published version or release asset.
2. Record full release notes under `releases/v<version>.md` and a short installer summary under `releases/v<version>.installer.txt`: 1–7 brief `- ` bullets (at most 100 characters each), then `Full details on GitHub repo:` and the repository URL on the next line. The build embeds this summary in the plugin manifest, and packaging copies it into the repository index. Update the README if features or installation change. Revalidate native bindings when the game or Dalamud changes.
3. Build, validate the package, and regenerate the repository index with PowerShell 7.2 or later:

```powershell
./tools/prepare-release.ps1
dotnet run --project ./BestiaryNav.Checks -c Release
dotnet run --project ./BestiaryNav.NativeChecks -c Release
```

The packaging script accepts `-Dotnet <path-to-dotnet>` and uses the installed Dalamud development libraries. `-SkipBuild` validates existing output without rebuilding. It checks DLL/manifest versions, ZIP structure and the packed DLL hash. It writes `pluginmaster.json` and ignored `artifacts/v<version>/` files: `latest.zip`, `BestiaryNav.json`, and `checksums.txt`. Game files, Dalamud DLLs and dependency plugins are not bundled.

The project manifest also supplies `IconUrl` and two `ImageUrls` through jsDelivr's GitHub CDN, pinned to `InstallerImageTag`. Unchanged images can keep their existing published tag across plugin updates. When changing images, update that tag and publish its `assets/` files before distributing the new manifest. Verify the public image downloads match the local assets. The packaging script validates PNG dimensions, URLs, and packed manifest fields; icons must be square and no larger than 512 × 512, and previews must fit within 730 × 380. `tools/prepare-images.ps1` reproduces the supplied-image crops and resizing on Windows.

## Publish

Commit the source, release notes and generated index. Publish the tag and its assets before advancing `main`, so subscribers do not see an index with a missing download. Replace `v0.5.3` in these example commands with the version being published:

```powershell
git add BestiaryNav README.md RELEASING.md releases tools assets pluginmaster.json .gitignore
git commit -m "Publish Bestiary Nav 0.5.3"
git tag v0.5.3
git push origin refs/tags/v0.5.3
gh release create v0.5.3 --repo TheKHD5/Bestiary-Nav --verify-tag --title "Bestiary Nav 0.5.3" --notes-file releases/v0.5.3.md artifacts/v0.5.3/latest.zip artifacts/v0.5.3/BestiaryNav.json artifacts/v0.5.3/checksums.txt
git push origin main
```

Verify that the raw index and every download URL work without GitHub authentication. Download the published ZIP, compare its SHA-256 with `checksums.txt`, and check the embedded manifest matches the repository version and API level. Finally, verify install/update through Dalamud's normal Plugin Installer with the development copy disabled.

## Stable subscription URL

```text
https://raw.githubusercontent.com/TheKHD5/Bestiary-Nav/main/pluginmaster.json
```

Users keep this subscription URL across releases; `AssemblyVersion` and the versioned asset URL in the index determine updates. vnavmesh and Lifestream remain separate installations required only for automatic travel.
