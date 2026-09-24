#Requires -Version 7.0

# .SYNOPSIS
#     Builds every shippable artifact into ./dist: the VSIX and the two NuGet
#     packages (the MSBuild SDK and the project templates).
#
# .DESCRIPTION
#     The VSIX needs the Visual Studio extension-development workload, so this
#     runs MSBuild.exe from the VS installation rather than the dotnet CLI. The
#     NuGet packages build with the dotnet CLI.
#
#     Artifacts are hashed at the end so a published file can be matched against
#     the build it came from.
#
# .NOTES
#     Idempotent: safe to run repeatedly. Nothing is published - see
#     docs/releasing.md for how the artifacts are distributed.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)

$ErrorActionPreference = 'Stop'
# The scripts live in scripts/, so the repository root is one level up.
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$languageServiceDirectory = Join-Path $repositoryRoot 'src/csharp/Orikago.LanguageService'

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
Get-ChildItem $OutputDirectory -File | Remove-Item -Force

# MIT requires the notice to ship with the software; the VSIX carries its own
# copy, so keep it identical to the repository LICENSE rather than letting the
# two drift.
$licenseCopy = Join-Path $languageServiceDirectory 'LICENSE.txt'
Copy-Item (Join-Path $repositoryRoot 'LICENSE') $licenseCopy -Force

# --- VSIX -------------------------------------------------------------------
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vswhereArguments = @(
    '-latest'
    '-products', '*'
    '-requires', 'Microsoft.VisualStudio.Workload.VisualStudioExtension'
    '-property', 'installationPath'
)
$vsPath = & $vswhere @vswhereArguments
if (-not $vsPath) {
    throw 'No Visual Studio installation with the extension development workload was found; ' +
        'the VSIX cannot be built here.'
}
$msbuild = Join-Path $vsPath 'MSBuild/Current/Bin/MSBuild.exe'
$vsixProject = Join-Path $languageServiceDirectory 'Orikago.LanguageService.csproj'

Write-Host "Building VSIX ($Configuration)" -ForegroundColor Cyan
$msbuildArguments = @(
    $vsixProject
    '/restore'
    "/p:Configuration=$Configuration"
    '/v:minimal'
    '/nologo'
    '/nodeReuse:false'
)
& $msbuild @msbuildArguments
if ($LASTEXITCODE -ne 0) { throw "VSIX build failed with exit code $LASTEXITCODE." }

$vsix = Join-Path $languageServiceDirectory "bin/$Configuration/Orikago.LanguageService.vsix"
if (-not (Test-Path $vsix)) { throw "Expected VSIX not found at $vsix." }
Copy-Item $vsix $OutputDirectory -Force

# --- NuGet packages ---------------------------------------------------------
$packageProjects = @(
    'sdk/Orikago.Sdk/Orikago.Sdk.csproj'
    'templates/Orikago.Templates.csproj'
)
foreach ($packageProject in $packageProjects) {
    $packageProjectPath = Join-Path $repositoryRoot $packageProject
    Write-Host "Packing $packageProject" -ForegroundColor Cyan
    dotnet pack $packageProjectPath -c $Configuration -o $OutputDirectory --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $packageProject with exit code $LASTEXITCODE."
    }
}

# --- Report -----------------------------------------------------------------
Write-Host "`nArtifacts in $OutputDirectory" -ForegroundColor Green
Get-ChildItem $OutputDirectory -File | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    "{0,-46} {1,10:N0} bytes  sha256:{2}" -f $_.Name, $_.Length, $hash.Substring(0, 16)
}
