#Requires -Version 7.0

# .SYNOPSIS
#     Packs Orikago.Sdk into the local NuGet feed (./packages) and clears the
#     global package cache entry so a re-pack of version 0.1.0-preview takes effect.
#
# .NOTES
#     Idempotent: safe to run repeatedly.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# The scripts live in scripts/, so the repository root is one level up.
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$feedDirectory = Join-Path $repositoryRoot 'packages'
$sdkProject = Join-Path $repositoryRoot 'sdk/Orikago.Sdk/Orikago.Sdk.csproj'

# Ensure the local feed directory exists (nuget.config points at it, and
# restore fails if a configured local source is missing).
$null = New-Item -ItemType Directory -Path $feedDirectory -Force

Write-Host "Packing $sdkProject -> $feedDirectory" -ForegroundColor Cyan
dotnet pack $sdkProject -c $Configuration -o $feedDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE"
}

# NuGet caches by id/version; version stays 0.1.0-preview, so evict the cached copy
# or consumers would keep restoring the stale package. The global-packages
# folder can be relocated (NUGET_PACKAGES / nuget.config), so ask NuGet for
# the real location instead of assuming %USERPROFILE%/.nuget/packages.
$globalPackagesListing = dotnet nuget locals global-packages --list
$globalPackages = $globalPackagesListing -replace '^global-packages:\s*', ''
$cacheCandidates = @(
    (Join-Path $globalPackages 'orikago.sdk'),
    (Join-Path $env:USERPROFILE '.nuget/packages/orikago.sdk')
) | Select-Object -Unique
foreach ($cacheEntry in $cacheCandidates) {
    if (Test-Path $cacheEntry) {
        Write-Host "Removing cached package: $cacheEntry" -ForegroundColor Cyan
        Remove-Item -Path $cacheEntry -Recurse -Force -Confirm:$false
    }
}

Write-Host 'Done. Orikago.Sdk 0.1.0-preview is available from the local feed.' -ForegroundColor Green
