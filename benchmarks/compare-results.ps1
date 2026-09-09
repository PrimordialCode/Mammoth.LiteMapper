param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $Baseline,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $Candidate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Stop-Comparison {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message,

        [Parameter(Mandatory = $true)]
        [int] $ExitCode
    )

    Write-Output $Message
    exit $ExitCode
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Value,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Context
    )

    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or
        ($property.Value -is [string] -and [string]::IsNullOrWhiteSpace($property.Value))) {
        Stop-Comparison "ERROR: missing $Context.$Name" 3
    }

    return $property.Value
}

function Read-Result {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    try {
        $result = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        Stop-Comparison "ERROR: invalid $Label benchmark result: $($_.Exception.Message)" 3
    }

    $null = Get-RequiredProperty $result 'metadata' $Label
    $null = Get-RequiredProperty $result 'benchmarks' $Label
    return $result
}

function Index-Benchmarks {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $Benchmarks,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $index = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($benchmark in $Benchmarks) {
        $id = [string](Get-RequiredProperty $benchmark 'id' $Label)
        $null = Get-RequiredProperty $benchmark 'meanNanoseconds' $Label
        $null = Get-RequiredProperty $benchmark 'standardErrorNanoseconds' $Label
        $null = Get-RequiredProperty $benchmark 'allocatedBytes' $Label
        if (-not $index.TryAdd($id, $benchmark)) {
            Stop-Comparison "ERROR: duplicate benchmark scenario '$id' in $Label result" 3
        }
    }

    return $index
}

$baselineResult = Read-Result $Baseline 'baseline'
$candidateResult = Read-Result $Candidate 'candidate'

$requiredMetadata = @(
    'runtime',
    'sdk',
    'cpu',
    'os',
    'configuration',
    'mapperly',
    'mapster',
    'automapper',
    'litemapper',
    'sourceRevision'
)

foreach ($name in $requiredMetadata) {
    $baselineValue = Get-RequiredProperty $baselineResult.metadata $name 'baseline.metadata'
    $candidateValue = Get-RequiredProperty $candidateResult.metadata $name 'candidate.metadata'

    if ($name -notin @('litemapper', 'sourceRevision') -and
        -not [object]::Equals($baselineValue, $candidateValue)) {
        Stop-Comparison "ERROR: incompatible benchmark environment ($name differs)" 3
    }
}

$baselineIndex = Index-Benchmarks @($baselineResult.benchmarks) 'baseline.benchmarks'
$candidateIndex = Index-Benchmarks @($candidateResult.benchmarks) 'candidate.benchmarks'

$baselineIds = @($baselineIndex.Keys | Sort-Object)
$candidateIds = @($candidateIndex.Keys | Sort-Object)
if ($baselineIds.Count -ne $candidateIds.Count -or
    [string]::Join("`n", $baselineIds) -cne [string]::Join("`n", $candidateIds)) {
    Stop-Comparison 'ERROR: benchmark scenario sets differ' 3
}

$blocked = [System.Collections.Generic.List[string]]::new()
$review = [System.Collections.Generic.List[string]]::new()
$z99 = 2.576

foreach ($id in $baselineIds) {
    $baselineBenchmark = $baselineIndex[$id]
    $candidateBenchmark = $candidateIndex[$id]
    $baselineAllocation = [long]$baselineBenchmark.allocatedBytes
    $candidateAllocation = [long]$candidateBenchmark.allocatedBytes

    if ($candidateAllocation -gt $baselineAllocation) {
        $blocked.Add("BLOCKED: allocation regression for $id ($baselineAllocation to $candidateAllocation bytes)")
    }

    $baselineMean = [double]$baselineBenchmark.meanNanoseconds
    $candidateMean = [double]$candidateBenchmark.meanNanoseconds
    $baselineError = [double]$baselineBenchmark.standardErrorNanoseconds
    $candidateError = [double]$candidateBenchmark.standardErrorNanoseconds

    if ($baselineMean -le 0 -or $candidateMean -le 0 -or $baselineError -lt 0 -or $candidateError -lt 0) {
        Stop-Comparison "ERROR: invalid numeric benchmark result for $id" 3
    }

    $combinedError = [Math]::Sqrt(($baselineError * $baselineError) + ($candidateError * $candidateError))
    $significant = $candidateMean -gt $baselineMean -and
        ($combinedError -eq 0 -or (($candidateMean - $baselineMean) / $combinedError) -gt $z99)
    $throughputLoss = (1.0 - ($baselineMean / $candidateMean)) * 100.0

    if ($significant -and $throughputLoss -gt 20.0) {
        $blocked.Add("BLOCKED: throughput loss for $id is $($throughputLoss.ToString('F2', [Globalization.CultureInfo]::InvariantCulture))%")
    }
    elseif ($significant -and $throughputLoss -gt 10.0) {
        $review.Add("REVIEW_REQUIRED: throughput loss for $id is $($throughputLoss.ToString('F2', [Globalization.CultureInfo]::InvariantCulture))%")
    }
}

foreach ($message in $blocked) {
    Write-Output $message
}

foreach ($message in $review) {
    Write-Output $message
}

if ($blocked.Count -gt 0) {
    exit 1
}

if ($review.Count -gt 0) {
    exit 2
}

Write-Output "PASS: $($baselineIds.Count) benchmark scenarios compared"
