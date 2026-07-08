#requires -Version 5
<#
.SYNOPSIS
  Verify the ACTUAL bits inside a QuinSlate .msixbundle before you hand it out.

.DESCRIPTION
  A green build is not proof the package contains current code. The MSIX packaging
  step can silently bundle a STALE per-architecture binary left over from an earlier
  session (this really happened: x64 was fresh, x86/arm64 were three commits old, and
  the bundle installed the old code on the target laptop). This script extracts every
  architecture's QuinSlate.Ui.dll out of the bundle and checks:

    1. Its embedded ProductVersion git SHA matches the commit you expect (HEAD by
       default). This is the universal tripwire: it catches stale bits regardless of
       what feature you changed.
    2. (Optional) a marker type/string is physically present in the assembly, proving
       a specific recent feature actually compiled in. Note: .NET metadata names are
       UTF-8, so we search the ASCII rendering, not UTF-16; searching UTF-16 gives
       false negatives on type names.

  Exits non-zero if any architecture fails, so it can gate a release.

  NOTE: keep this file ASCII-only. Windows PowerShell 5.1 mis-decodes non-ASCII
  punctuation (em dashes, etc.) in .ps1 files and can corrupt parsing.

.PARAMETER Bundle
  Path to the .msixbundle to inspect.

.PARAMETER ExpectedCommit
  Full git SHA the binaries should be built from. Defaults to `git rev-parse HEAD`.

.PARAMETER Marker
  Optional string (e.g. a type name from a recent commit) that must appear in
  QuinSlate.Ui.dll. Omit to check only the version stamp.

.PARAMETER Architectures
  Which arch payloads to check. Defaults to x64, x86, arm64.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$Bundle,
  [string]$ExpectedCommit,
  [string]$Marker,
  [string[]]$Architectures = @("x64","x86","arm64")
)

$ErrorActionPreference = "Stop"

function Find-Tool([string]$name) {
  $hit = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter $name -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $hit) {
    $hit = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter $name -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
  }
  if (-not $hit) { throw "Could not locate $name. Install the Windows SDK or restore Microsoft.Windows.SDK.BuildTools." }
  return $hit.FullName
}

if (-not (Test-Path $Bundle)) { throw "Bundle not found: $Bundle" }
if (-not $ExpectedCommit) { $ExpectedCommit = (& git rev-parse HEAD).Trim() }

$makeappx = Find-Tool "makeappx.exe"
$work = Join-Path $env:TEMP "qs-verify-$(Get-Random)"
New-Item -ItemType Directory -Force -Path $work | Out-Null

& $makeappx unbundle /p $Bundle /d "$work\b" /o | Out-Null

$allOk = $true
foreach ($arch in $Architectures) {
  $msix = Get-ChildItem "$work\b" -Filter "*_$arch.msix" | Select-Object -First 1
  if (-not $msix) { Write-Host "[$arch] MISSING payload in bundle" -ForegroundColor Red; $allOk = $false; continue }
  & $makeappx unpack /p $msix.FullName /d "$work\$arch" /o | Out-Null
  $dll = "$work\$arch\QuinSlate.Ui.dll"
  if (-not (Test-Path $dll)) { Write-Host "[$arch] QuinSlate.Ui.dll missing" -ForegroundColor Red; $allOk = $false; continue }

  $product = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
  $stampedSha = ""
  if ($product -match '\+([0-9a-f]{7,40})') { $stampedSha = $Matches[1] }
  $shaOk = $stampedSha -and $ExpectedCommit.StartsWith($stampedSha)

  $markerOk = $true
  if ($Marker) {
    $ascii = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dll))
    $markerOk = $ascii.Contains($Marker)
  }

  $ok = $shaOk -and $markerOk
  if (-not $ok) { $allOk = $false }
  $color = if ($ok) { "Green" } else { "Red" }
  $markerText = if ($Marker) { "  marker '$Marker'=$markerOk" } else { "" }
  $shortExpected = $ExpectedCommit.Substring(0,[Math]::Min(7,$ExpectedCommit.Length))
  Write-Host ("[{0}] version={1}  sha={2} (expected {3}) shaOk={4}{5}" -f $arch, $product, $stampedSha, $shortExpected, $shaOk, $markerText) -ForegroundColor $color
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

if ($allOk) {
  Write-Host ("VERIFY: PASS - every architecture matches {0}" -f $ExpectedCommit.Substring(0,7)) -ForegroundColor Green
  exit 0
}
else {
  Write-Host "VERIFY: FAIL - bundle contains stale or wrong bits. Do NOT ship it. Clean-rebuild (see SKILL.md)." -ForegroundColor Red
  exit 1
}
