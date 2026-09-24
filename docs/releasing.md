# Releasing

Three artifacts ship together, and they do **not** all go to the same place.

| Artifact | What it is | Where it goes | Why |
|---|---|---|---|
| `Orikago.LanguageService.vsix` | The Visual Studio extension | **GitHub Release asset** | GitHub Packages has no VSIX registry — it only hosts npm, NuGet, Maven, Gradle, RubyGems and container images. A release asset is the standard distribution point for a VSIX outside the Marketplace. |
| `Orikago.Sdk.<version>.nupkg` | The MSBuild project SDK | **nuget.org** *and* release asset | This one genuinely is a NuGet package; from nuget.org `Sdk="Orikago.Sdk/<version>"` resolves with no extra feed configuration. |
| `Orikago.Templates.<version>.nupkg` | `dotnet new` templates | **nuget.org** *and* release asset | Same reasoning. |

## Build

```powershell
./scripts/build-release.ps1  # -> ./dist, with SHA-256 for each file
```

The VSIX requires the Visual Studio extension-development workload, so this
must run on a machine with Visual Studio installed — it cannot be built by the
`dotnet` CLI alone, and a plain GitHub-hosted runner will not have the matching
VS version either.

## Publish

```powershell
# 1. Tag the commit being released
git tag v0.1.0
git push origin v0.1.0

# 2. Create the release and attach every artifact
gh release create v0.1.0 (Get-ChildItem ./dist/* | ForEach-Object FullName) `
    --title "v0.1.0" --notes-file release-notes.md

# 3. Push the NuGet packages to nuget.org (API key scoped to Orikago.*)
dotnet nuget push "./dist/*.nupkg" --api-key $env:NUGET_ORG_API_KEY --source https://api.nuget.org/v3/index.json
```

## Consuming the published SDK

The packages are on nuget.org, so the default NuGet source is enough:

```powershell
dotnet new install Orikago.Templates::0.1.0-preview
```

## Versioning

The version lives in three places and they are deliberately independent:

- `sdk/Orikago.Sdk/Orikago.Sdk.csproj` — the SDK package version, referenced by every `.goproj` as `Sdk="Orikago.Sdk/<version>"`, so bumping it is a breaking change for existing projects.
- `templates/Orikago.Templates.csproj` — the template package version.
- `src/csharp/Orikago.LanguageService/source.extension.vsixmanifest` — the extension version. **VSIXInstaller silently no-ops when the installed version matches**, which is why `install-vsix.ps1` uninstalls first rather than relying on a version bump.

The git tag names the release as a whole and does not have to match any of
them.


