<#
.SYNOPSIS
Packs (and optionally publishes) the six LifeOS.Pdf packages without GitHub Actions.

.DESCRIPTION
The version comes from -Version or, when omitted, from VersionPrefix in Directory.Build.props.
The API key is never printed: pass -ApiKey or -ApiKeyFile, or set the NUGET_API_KEY environment
variable. Use -PackOnly to build the .nupkg files and stop before publishing.

.EXAMPLE
pwsh tools/release.ps1 -PackOnly

.EXAMPLE
$env:NUGET_API_KEY = 'nuget-api-key'; pwsh tools/release.ps1

.EXAMPLE
pwsh tools/release.ps1 -Version 1.1.0 -ApiKeyFile C:\temp\nuget.key
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $ApiKey,
    [string] $ApiKeyFile,
    [string] $Source = 'https://api.nuget.org/v3/index.json',
    [string] $OutputDir = 'artifacts',
    [switch] $PackOnly,
    [switch] $AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $projects = @(
        'src/LifeOS.Pdf',
        'src/LifeOS.Pdf.Signing',
        'src/LifeOS.Pdf.Rendering',
        'src/LifeOS.Pdf.Barcode.Scanning',
        'src/LifeOS.Pdf.Ocr',
        'src/LifeOS.Pdf.Scanning',
        'src/LifeOS.Pdf.All'
    )

    function Get-PackageId([string] $fileName) {
        return $fileName -replace '\.[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?\.nupkg$', ''
    }

    function Test-PackageExists([string] $id) {
        try {
            $uri = 'https://api.nuget.org/v3-flatcontainer/' + $id.ToLowerInvariant() + '/index.json'
            Invoke-RestMethod -Uri $uri -TimeoutSec 10 | Out-Null
            return $true
        }
        catch {
            return $false
        }
    }

    if (-not $Version) {
        [xml] $props = Get-Content 'Directory.Build.props'
        $node = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
        $Version = if ($node) { $node.InnerText.Trim() } else { '' }
        if (-not $Version) {
            throw 'VersionPrefix is missing from Directory.Build.props; pass -Version.'
        }
    }

    if (-not $PackOnly -and -not $AllowDirty) {
        if (Get-Command git -ErrorAction SilentlyContinue) {
            $dirty = git status --porcelain 2>$null
            if ($LASTEXITCODE -eq 0 -and $dirty) {
                throw 'The working tree has uncommitted changes. Commit them first, or pass -AllowDirty.'
            }
        }
    }

    if (-not $PackOnly) {
        $key = $ApiKey
        if (-not $key -and $ApiKeyFile) {
            $key = (Get-Content -LiteralPath $ApiKeyFile -Raw).Trim()
        }

        if (-not $key) {
            $key = $env:NUGET_API_KEY
        }

        if (-not $key) {
            throw 'No NuGet API key. Set NUGET_API_KEY, pass -ApiKeyFile, or pass -ApiKey (scope: "Push new packages and package versions").'
        }
    }

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    Get-ChildItem -Path $OutputDir -Filter *.nupkg -ErrorAction SilentlyContinue | Remove-Item -Force

    Write-Host "Packing version $Version into $OutputDir"
    foreach ($project in $projects) {
        dotnet pack $project -c Release -o $OutputDir -p:Version=$Version --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet pack failed for $project"
        }
    }

    $packages = Get-ChildItem -Path $OutputDir -Filter *.nupkg | Sort-Object Name
    Write-Host ''
    Write-Host 'Packages:'
    foreach ($package in $packages) {
        Write-Host ("  {0,-46} {1,8:N0} KB" -f $package.Name, ($package.Length / 1KB))
    }

    if ($PackOnly) {
        Write-Host ''
        Write-Host "Done (pack only). Publish with: pwsh tools/release.ps1 -Version $Version"
        return
    }

    Write-Host ''
    foreach ($package in $packages) {
        $id = Get-PackageId $package.Name
        if (-not (Test-PackageExists $id)) {
            Write-Host "  note: $id is new on nuget.org; the key must allow 'Push new packages and package versions'."
        }
    }

    Write-Host ''
    Write-Host "Publishing to $Source"
    foreach ($package in $packages) {
        dotnet nuget push $package.FullName --api-key $key --source $Source --skip-duplicate
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet nuget push failed for $($package.Name). A 403 on a new package id means the key is scoped to 'Push only package versions'; create a key with 'Push new packages and package versions'."
        }
    }

    Write-Host ''
    Write-Host "Published $($packages.Count) package(s) version $Version."
}
finally {
    Pop-Location
}
