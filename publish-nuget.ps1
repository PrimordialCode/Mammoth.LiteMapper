param(
    [string] $ApiKey,

    [string] $Source,

    [string] $OutputDirectory,

    [switch] $SkipValidation,

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0


function Read-RequiredText {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [string] $DefaultValue
    )

    $prompt = if ([string]::IsNullOrWhiteSpace($DefaultValue)) { $Name } else { "$Name [$DefaultValue]" }
    $value = Read-Host $prompt
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = $DefaultValue
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is required."
    }

    return $value
}

function Read-BooleanParameter {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [bool] $DefaultValue
    )

    $defaultText = if ($DefaultValue) { 'Y/n' } else { 'y/N' }
    while ($true) {
        $value = Read-Host "${Name}? [$defaultText]"
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $DefaultValue
        }

        if ($value -match '^(y|yes|true|1)$') {
            return $true
        }

        if ($value -match '^(n|no|false|0)$') {
            return $false
        }

        Write-Host "Enter yes or no."
    }
}
function Assert-NativeSuccess {
    param([Parameter(Mandatory = $true)][string] $Command)

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

$repoRoot = $PSScriptRoot
Set-Location $repoRoot
if (-not $PSBoundParameters.ContainsKey('Source')) {
    $Source = Read-RequiredText -Name 'NuGet source' -DefaultValue 'https://api.nuget.org/v3/index.json'
}

if (-not $PSBoundParameters.ContainsKey('OutputDirectory')) {
    $OutputDirectory = Read-RequiredText -Name 'Package output directory' -DefaultValue 'artifacts/packages'
}

if (-not $PSBoundParameters.ContainsKey('SkipValidation')) {
    $SkipValidation = Read-BooleanParameter -Name 'Skip restore/build/test validation' -DefaultValue $false
}

if (-not $PSBoundParameters.ContainsKey('DryRun')) {
    $DryRun = Read-BooleanParameter -Name 'Dry run' -DefaultValue $true
}

if (-not $PSBoundParameters.ContainsKey('ApiKey')) {
    $apiKeyPrompt = if ($DryRun) { 'NuGet API key (optional for dry run)' } else { 'NuGet API key' }
    $ApiKey = Read-Host $apiKeyPrompt
    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        $ApiKey = $env:NUGET_API_KEY
    }
}
& dotnet tool restore
Assert-NativeSuccess 'dotnet tool restore'

$gitVersionOutput = & dotnet gitversion /output json
if ($LASTEXITCODE -ne 0) {
    throw "Command failed with exit code ${LASTEXITCODE}: dotnet gitversion /output json"
}

$gitVersionJson = $gitVersionOutput | ConvertFrom-Json
$version = $gitVersionJson.SemVer
$assemblyVersion = $gitVersionJson.AssemblySemVer
$fileVersion = $gitVersionJson.AssemblySemFileVer
$informationalVersion = $gitVersionJson.InformationalVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'GitVersion did not return a SemVer value.'
}

$isPrerelease = $version -match '-'
$resolvedChannel = if ($isPrerelease) { 'beta' } else { 'stable' }

if (-not $DryRun -and [string]::IsNullOrWhiteSpace($ApiKey)) {
    throw 'NUGET_API_KEY or -ApiKey is required unless -DryRun is used.'
}

Write-Host "GitVersion SemVer: $version"
Write-Host "GitVersion InformationalVersion: $informationalVersion"
Write-Host "Publish channel: $resolvedChannel"

if (-not $SkipValidation) {
    & dotnet restore Mammoth.LiteMapper.sln
    Assert-NativeSuccess 'dotnet restore Mammoth.LiteMapper.sln'
    & dotnet build Mammoth.LiteMapper.sln -c Release --no-restore `
        /p:Version=$version `
        /p:AssemblyVersion=$assemblyVersion `
        /p:FileVersion=$fileVersion `
        /p:InformationalVersion=$informationalVersion `
        /p:ContinuousIntegrationBuild=true
    Assert-NativeSuccess 'dotnet build Mammoth.LiteMapper.sln -c Release --no-restore with GitVersion properties'
    & dotnet test Mammoth.LiteMapper.sln -c Release --no-build
    Assert-NativeSuccess 'dotnet test Mammoth.LiteMapper.sln -c Release --no-build'
}

$resolvedOutput = Join-Path $repoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$packProjects = @(
    'src/Mammoth.LiteMapper.Abstractions/Mammoth.LiteMapper.Abstractions.csproj',
    'src/Mammoth.LiteMapper.Generator/Mammoth.LiteMapper.Generator.csproj',
    'src/Mammoth.LiteMapper/Mammoth.LiteMapper.csproj'
)

foreach ($project in $packProjects) {
    & dotnet pack $project -c Release --no-restore -o $resolvedOutput `
        /p:PackageVersion=$version `
        /p:Version=$version `
        /p:AssemblyVersion=$assemblyVersion `
        /p:FileVersion=$fileVersion `
        /p:InformationalVersion=$informationalVersion `
        /p:ContinuousIntegrationBuild=true
    Assert-NativeSuccess "dotnet pack $project -c Release --no-restore -o $resolvedOutput with GitVersion properties"
}

$packages = Get-ChildItem -Path $resolvedOutput -Filter "Mammoth.LiteMapper*.$version.nupkg" |
    Where-Object { $_.Name -notlike '*.symbols.nupkg' } |
    Sort-Object Name

if ($packages.Count -ne 3) {
    throw "Expected 3 packages for version $version, found $($packages.Count)."
}

foreach ($package in $packages) {
    if ($DryRun) {
        Write-Host "Dry run: would publish $($package.FullName) to $Source"
        continue
    }

    & dotnet nuget push $package.FullName --source $Source --api-key $ApiKey --skip-duplicate
    Assert-NativeSuccess "dotnet nuget push $($package.FullName) --source $Source --api-key *** --skip-duplicate"
}
