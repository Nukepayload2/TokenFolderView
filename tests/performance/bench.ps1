<#
.SYNOPSIS
    Dev-time performance benchmark: TokenVisualizer.Core EncodeCount vs the
    Python `tokenizers` reference, on the deepseek tokenizer.

.DESCRIPTION
    Runs the throwaway .NET console (scripts/bench, ProjectReference into
    TokenVisualizer.Core) and the Python reference (scripts/bench_ref.py) over
    the same deterministic text, then prints a comparison table
    (input MB, tokens, MB/s, tokens/s) for .NET vs Python.

    With -Path, both harnesses instead build the text from a real folder/file
    (recursively, text files only) and report token counts -- this doubles as
    the parity spot-check (our EncodeCount total vs the Python reference).

    The scripts/bench console project is dev-only and is NOT part of the slnx.

.EXAMPLE
    ./scripts/bench.ps1                    # synthetic ~2 MB mixed text
    ./scripts/bench.ps1 -Path .\TokenVisualizer.Core   # real-folder parity + perf
#>
param(
    [string]$Path = "",
    [string]$Config = "Release",
    [int]$Iterations = 3,
    [int]$CacheCapacity = -1,
    [int]$CacheMaxWord = -1
)

$ErrorActionPreference = "Stop"

function Parse-Line([string]$line) {
    $h = @{}
    $body = $line.Substring($line.IndexOf("|") + 1)
    foreach ($kv in $body.Split("|")) {
        $parts = $kv.Split("=", 2)
        if ($parts.Count -eq 2) { $h[$parts[0]] = $parts[1] }
    }
    return $h
}

function Get-ResultLine([string]$text, [string]$prefix) {
    foreach ($ln in $text -split "`r?`n") {
        if ($ln.StartsWith($prefix + "|")) { return $ln }
    }
    return $null
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$benchProj = Join-Path $PSScriptRoot "bench\Bench.vbproj"
$refPy = Join-Path $PSScriptRoot "bench_ref.py"
$tokenizerJson = Join-Path $repoRoot "deepseek-v4-flash\tokenizer.json"

if (-not (Test-Path $tokenizerJson)) {
    Write-Host "ERROR: deepseek tokenizer.json not found at $tokenizerJson"
    exit 2
}

Write-Host "== TokenVisualizer.Core vs Python tokenizers benchmark =="
Write-Host ("Tokenizer : " + $tokenizerJson)
if ($Path) {
    Write-Host ("Input     : real folder/file " + $Path)
} else {
    Write-Host "Input     : synthetic ~2 MB mixed text (ASCII + CJK + digits + punctuation + emoji)"
}
Write-Host ""

# ---- .NET side ----
Write-Host "[1/2] dotnet bench (config=$Config) ..."
$dotnetArgs = @("run", "-c", $Config, "--project", $benchProj, "--",
    "--tokenizer", $tokenizerJson, "--iterations", [string]$Iterations)
if ($Path) { $dotnetArgs += @("--path", $Path) }
if ($CacheCapacity -ge 0) { $dotnetArgs += @("--cache-capacity", [string]$CacheCapacity) }
if ($CacheMaxWord -ge 0) { $dotnetArgs += @("--cache-max-word", [string]$CacheMaxWord) }
$dotnetOut = & dotnet @dotnetArgs 2>&1 | Out-String
$dotnetLine = Get-ResultLine $dotnetOut "DOTNET"
if (-not $dotnetLine) {
    Write-Host "ERROR: dotnet bench produced no result line. Output:"
    Write-Host $dotnetOut
    exit 1
}
$dotnet = Parse-Line $dotnetLine

# ---- Python side ----
Write-Host "[2/2] python reference ..."
$pyArgs = @($refPy, "--iterations", [string]$Iterations)
if ($Path) { $pyArgs += @("--path", $Path) }
$pyOut = & python @pyArgs 2>&1 | Out-String
$pyLine = Get-ResultLine $pyOut "PYTHON"
if (-not $pyLine) {
    Write-Host "ERROR: python bench produced no result line. Output:"
    Write-Host $pyOut
    exit 1
}
$python = Parse-Line $pyLine

# ---- Report ----
Write-Host ""
Write-Host "input_mb    tokens        elapsed_ms    MB/s        tokens/s"
Write-Host ("---------   -----------   -----------   --------    -----------")
Write-Host ("dotnet {0,9:N2} {1,11:N0} {2,11:F1} {3,9:N1} {4,12:N0}" -f `
    [double]$dotnet["input_mb"], [long]$dotnet["tokens"], [double]$dotnet["elapsed_ms"],
    [double]$dotnet["mb_per_s"], [double]$dotnet["tokens_per_s"])
Write-Host ("python {0,9:N2} {1,11:N0} {2,11:F1} {3,9:N1} {4,12:N0}" -f `
    [double]$python["input_mb"], [long]$python["tokens"], [double]$python["elapsed_ms"],
    [double]$python["mb_per_s"], [double]$python["tokens_per_s"])
Write-Host ""

$mbpsRatio = [double]$dotnet["mb_per_s"] / [double]$python["mb_per_s"]
$tpsRatio = [double]$dotnet["tokens_per_s"] / [double]$python["tokens_per_s"]
Write-Host ("dotnet/python MB/s ratio : {0:N3}x" -f $mbpsRatio)
Write-Host ("dotnet/python tok/s ratio: {0:N3}x" -f $tpsRatio)

$tokenDiff = [long]$dotnet["tokens"] - [long]$python["tokens"]
$tokenDiffPct = if ([long]$python["tokens"] -ne 0) { 100.0 * $tokenDiff / [long]$python["tokens"] } else { 0.0 }
Write-Host ("token count parity        : dotnet={0} python={1} diff={2} ({3:F4}%)" -f `
    [long]$dotnet["tokens"], [long]$python["tokens"], $tokenDiff, $tokenDiffPct)

if ($tokenDiff -eq 0) {
    Write-Host "PARITY: OK (EncodeCount total matches the Python reference exactly)"
} elseif ([Math]::Abs($tokenDiffPct) -lt 0.01) {
    Write-Host "PARITY: OK (diff within 0.01%)"
} else {
    Write-Host "PARITY: MISMATCH - investigate!"
}

if ($mbpsRatio -ge 1.0) {
    Write-Host "PERF: .NET is faster than or equal to Python ($($mbpsRatio.ToString('F2'))x)"
} elseif ($mbpsRatio -ge 0.1) {
    Write-Host "PERF: .NET is within the same order of magnitude ($($mbpsRatio.ToString('F3'))x of Python)"
} else {
    Write-Host "PERF: .NET is more than 10x slower than Python ($($mbpsRatio.ToString('F3'))x)"
}
