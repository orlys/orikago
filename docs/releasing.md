# Releasing

Three artifacts ship together, and they do **not** all go to the same place.

| Artifact | What it is | Where it goes | Why |
|---|---|---|---|
| `Orikago.LanguageService.vsix` | The Visual Studio extension | **GitHub Release asset** | GitHub Packages has no VSIX registry — it only hosts npm, NuGet, Maven, Gradle, RubyGems and container images. A release asset is the standard distribution point for a VSIX outside the Marketplace. |
| `Orikago.Sdk.<version>.nupkg` | The MSBuild project SDK | **nuget.org** *and* release asset | This one genuinely is a NuGet package; from nuget.org `Sdk="Orikago.Sdk/<version>"` resolves with no extra feed configuration. |
| `Orikago.Templates.<version>.nupkg` | `dotnet new` templates | **nuget.org** *and* release asset | Same reasoning. |

## Build

```powershell
# Writes to ./dist, with a SHA-256 for each file
./scripts/build-release.ps1
```

The VSIX requires the Visual Studio extension-development workload, so this
must run on a machine with Visual Studio installed — it cannot be built by the
`dotnet` CLI alone, and a plain GitHub-hosted runner will not have the matching
VS version either.

## Publish

```powershell
# 1. Tag the commit being released. The tag also runs .github/workflows/publish.yml,
#    which packs both NuGet packages and pushes them to nuget.org.
git tag v0.2.0
git push origin v0.2.0

# 2. Create the release and attach every artifact
gh release create v0.2.0 (Get-ChildItem ./dist/* | ForEach-Object FullName) `
    --title "v0.2.0" --notes-file release-notes.md
```

The workflow authenticates with nuget.org Trusted Publishing, so there is no API key to
store. It needs one policy on nuget.org: repository owner `orlys`, repository `orikago`,
workflow file `publish.yml`.

## Consuming the published SDK

The packages are on nuget.org, so the default NuGet source is enough:

```powershell
dotnet new install Orikago.Templates::0.2.0-preview
```

## Versioning

The version lives in three places and they are deliberately independent:

- `sdk/Orikago.Sdk/Orikago.Sdk.csproj` — the SDK package version, referenced by every `.goproj`
  as `Sdk="Orikago.Sdk/<version>"`, so bumping it is a breaking change for existing projects.
- `templates/Orikago.Templates.csproj` — the template package version.
- `src/csharp/Orikago.LanguageService/source.extension.vsixmanifest` — the extension version.
  **VSIXInstaller silently no-ops when the installed version matches**, which is why
  `install-vsix.ps1` uninstalls first rather than relying on a version bump.

The git tag names the release as a whole and does not have to match any of
them.