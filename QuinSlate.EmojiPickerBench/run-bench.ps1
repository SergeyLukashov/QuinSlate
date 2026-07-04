param(
    [string[]]$Scenarios = @("cold-all", "cold-paced", "warm-repeat", "warmed-open", "search", "scroll"),
    [int]$Iterations = 3,
    [string]$Label = "run"
)

$ErrorActionPreference = "Stop"
$benchDir = $PSScriptRoot
$resultsDir = Join-Path $benchDir "results"
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory $resultsDir | Out-Null }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outFile = Join-Path $resultsDir "$Label-$stamp.jsonl"

Write-Host "Building bench..."
dotnet build (Join-Path $benchDir "QuinSlate.EmojiPickerBench.csproj") -v:q -nologo
if ($LASTEXITCODE -ne 0) { throw "bench build failed" }

# Standalone builds output to bin\Debug\...; solution builds (-p:Platform=x64)
# to bin\x64\Debug\... — pick the freshest exe.
$exe = Get-ChildItem (Join-Path $benchDir "bin") -Recurse -Filter "QuinSlate.EmojiPickerBench.exe" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $exe) { throw "bench exe not found under $benchDir\bin" }

foreach ($scenario in $Scenarios) {
    for ($i = 1; $i -le $Iterations; $i++) {
        Write-Host "[$scenario] iteration $i..."
        $proc = Start-Process -FilePath $exe -ArgumentList @($scenario, "`"$outFile`"") -PassThru
        if (-not $proc.WaitForExit(60000)) {
            $proc.Kill()
            Write-Host "  TIMEOUT (killed)"
        }
    }
}

Write-Host ""
Write-Host "=== Results: $outFile ==="
Get-Content $outFile | ForEach-Object {
    $r = $_ | ConvertFrom-Json
    if ($r.error) { Write-Host "$($r.scenario): ERROR $($r.error)"; return }
    $parts = @()
    foreach ($prefix in @("attach", "firstAttach", "secondAttach", "searchPhase", "scrollPhase")) {
        $settle = $r."${prefix}SettleMs"
        if ($null -ne $settle) {
            $parts += "${prefix}: settle=$($settle)ms max=$($r."${prefix}MaxDeltaMs")ms p95=$($r."${prefix}P95DeltaMs")ms >33ms=$($r."${prefix}FramesOver33Ms") >100ms=$($r."${prefix}FramesOver100Ms")"
        }
    }
    if ($r.searchUiCostsMs) {
        $maxUi = ($r.searchUiCostsMs | Measure-Object -Maximum).Maximum
        $parts += "searchUiMax=$([math]::Round($maxUi,1))ms"
    }
    Write-Host "$($r.scenario): $($parts -join ' | ')"
}
