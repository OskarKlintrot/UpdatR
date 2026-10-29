<#
.SYNOPSIS
Compares startup time for two Release builds of the dotnet-updatr CLI.

.DESCRIPTION
Runs isolated CLI scenarios in fresh processes and compares median elapsed time. Build the
baseline first and copy its complete output directory before building the candidate, then pass
each build's dotnet-updatr.dll path. The update scenario dispatches to a missing target and exits
before project evaluation or external package queries.

.PARAMETER BaselineDll
Path to the baseline build's dotnet-updatr.dll.

.PARAMETER CandidateDll
Path to the candidate build's dotnet-updatr.dll.

.PARAMETER Iterations
Number of measured process runs per build and scenario.

.PARAMETER WarmupRuns
Number of unmeasured process runs per build and scenario.

.EXAMPLE
.\tools\Benchmark-CliStartup.ps1 -BaselineDll $baselineDll -CandidateDll $candidateDll

Negative ChangePercent values mean the candidate is faster.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BaselineDll,

    [Parameter(Mandatory)]
    [string] $CandidateDll,

    [ValidateRange(1, 1000)]
    [int] $Iterations = 15,

    [ValidateRange(0, 100)]
    [int] $WarmupRuns = 1
)

$ErrorActionPreference = 'Stop'

foreach ($dll in @($BaselineDll, $CandidateDll)) {
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "CLI assembly not found: $dll"
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "updatr-cli-startup-$([Guid]::NewGuid())"
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $configPath = Join-Path $tempRoot '.updatrrc'
    Set-Content -LiteralPath $configPath -Encoding utf8 -Value '{}'

    $missingProjectPath = Join-Path $tempRoot 'does-not-exist.csproj'
    $scenarios = @(
        [pscustomobject]@{
            Name = 'Root help'
            Arguments = @('--help')
            ExpectedExitCode = 0
        }
        [pscustomobject]@{
            Name = 'Config init help'
            Arguments = @('config', 'init', '--help')
            ExpectedExitCode = 0
        }
        [pscustomobject]@{
            Name = 'Config validate'
            Arguments = @('config', 'validate', $configPath)
            ExpectedExitCode = 0
        }
        [pscustomobject]@{
            Name = 'Update dispatch'
            Arguments = @($missingProjectPath, '--dry-run', '--output', 'json')
            ExpectedExitCode = 1
        }
    )

    function Invoke-CliScenario {
        param(
            [Parameter(Mandatory)]
            [string] $Dll,

            [Parameter(Mandatory)]
            [string[]] $Arguments,

            [Parameter(Mandatory)]
            [int] $ExpectedExitCode
        )

        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = 'dotnet'
        $startInfo.WorkingDirectory = $tempRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.ArgumentList.Add((Resolve-Path -LiteralPath $Dll).Path)

        foreach ($argument in $Arguments) {
            $startInfo.ArgumentList.Add($argument)
        }

        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()

        if (-not $process.Start()) {
            throw "Could not start dotnet for '$Dll'."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stopwatch.Stop()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()

        if ($process.ExitCode -ne $ExpectedExitCode) {
            throw "CLI exited with code $($process.ExitCode).`n$stdout`n$stderr"
        }

        return $stopwatch.Elapsed.TotalMilliseconds
    }

    function Get-Median {
        param([double[]] $Values)

        $sortedValues = @($Values | Sort-Object)
        $middle = [int][Math]::Floor($sortedValues.Count / 2)

        if ($sortedValues.Count % 2 -eq 0) {
            return ($sortedValues[$middle - 1] + $sortedValues[$middle]) / 2
        }

        return $sortedValues[$middle]
    }

    $results = foreach ($scenario in $scenarios) {
        $baselineTimes = [Collections.Generic.List[double]]::new()
        $candidateTimes = [Collections.Generic.List[double]]::new()
        $scenarioParameters = @{
            Arguments = $scenario.Arguments
            ExpectedExitCode = $scenario.ExpectedExitCode
        }

        for ($i = 0; $i -lt $WarmupRuns; $i++) {
            Invoke-CliScenario -Dll $BaselineDll @scenarioParameters | Out-Null
            Invoke-CliScenario -Dll $CandidateDll @scenarioParameters | Out-Null
        }

        for ($i = 0; $i -lt $Iterations; $i++) {
            if ($i % 2 -eq 0) {
                $baselineTimes.Add((Invoke-CliScenario -Dll $BaselineDll @scenarioParameters))
                $candidateTimes.Add((Invoke-CliScenario -Dll $CandidateDll @scenarioParameters))
            }
            else {
                $candidateTimes.Add((Invoke-CliScenario -Dll $CandidateDll @scenarioParameters))
                $baselineTimes.Add((Invoke-CliScenario -Dll $BaselineDll @scenarioParameters))
            }
        }

        $baselineMedian = Get-Median -Values $baselineTimes.ToArray()
        $candidateMedian = Get-Median -Values $candidateTimes.ToArray()

        [pscustomobject]@{
            Scenario = $scenario.Name
            BaselineMs = [Math]::Round($baselineMedian, 2)
            CandidateMs = [Math]::Round($candidateMedian, 2)
            ChangePercent = [Math]::Round((($candidateMedian - $baselineMedian) / $baselineMedian) * 100, 1)
        }
    }

    Write-Output 'Median process elapsed time in milliseconds; negative change means faster.'
    $results | Format-Table -AutoSize
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
