#requires -Version 5
<#
.SYNOPSIS
  Build, sign, verify, and stage a sideloadable QuinSlate MSIX bundle for another machine.

.DESCRIPTION
  One command that encodes every trap we hit doing this by hand. In order it:
    1. Ensures a self-signed signing cert whose subject EXACTLY matches the manifest
       Publisher (a Store GUID CN). A mismatch means the signature won't bind to the
       package identity and Windows refuses the install.
    2. CLEANS bin/obj so all three architectures recompile from current source. This
       is not optional caution: MSBuild happily reuses a stale per-arch publish output
       from an earlier session and bundles old code silently.
    3. Builds + packages via VS MSBuild with the exact switches that work (see the
       "Why these flags" note below).
    4. Signs the produced .msixbundle with signtool (we sign here, not via MSBuild;
       MSBuild's PackageCertificatePassword import is flaky and fails with APPX0105).
    5. VERIFIES the actual bits inside the bundle against HEAD (verify_bundle.ps1).
       If verification fails the script stops before staging, so a stale bundle never
       reaches you.
    6. Stages the shareable deliverables (bundle + public .cer + INSTALL.md) into the
       dist folder and keeps the private key out of the shared set.

  NOTE: keep this file ASCII-only. Windows PowerShell 5.1 mis-decodes non-ASCII
  punctuation (em dashes, etc.) in .ps1 files and can corrupt parsing.

.PARAMETER ProjectPath
  Path to QuinSlate.Ui.csproj. Defaults to the repo's QuinSlate.Ui\QuinSlate.Ui.csproj.

.PARAMETER DistDir
  Where to stage deliverables. Defaults to <repo-parent>\dist.

.PARAMETER Marker
  Optional type/string a recent commit introduced, forwarded to verify_bundle.ps1 to
  prove that specific feature compiled in (e.g. "EmojiSpriteAtlas").

.PARAMETER CertPassword
  Password for the self-signed PFX. Defaults to a random per-run value so nothing
  secret-looking is committed. The PFX only guards a local sideload-trust cert whose
  private key never leaves the machine, so the password has no value to an attacker.
#>
[CmdletBinding()]
param(
  [string]$ProjectPath,
  [string]$DistDir,
  [string]$Marker,
  [string]$CertPassword
)

$ErrorActionPreference = "Stop"
if (-not $CertPassword) { $CertPassword = [System.Guid]::NewGuid().ToString("N") }
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- Resolve paths ---
if (-not $ProjectPath) {
  $repo = (& git -C $scriptDir rev-parse --show-toplevel).Trim()
  $ProjectPath = Join-Path $repo "QuinSlate.Ui\QuinSlate.Ui.csproj"
}
$ProjectDir = Split-Path -Parent $ProjectPath
$repoRoot   = (& git -C $ProjectDir rev-parse --show-toplevel).Trim()
if (-not $DistDir) { $DistDir = Join-Path (Split-Path -Parent $repoRoot) "dist" }
$keysDir = Join-Path $DistDir "_private-keys-do-not-share"
New-Item -ItemType Directory -Force -Path $DistDir, $keysDir | Out-Null

# --- Read identity from the manifest (single source of truth) ---
$manifestPath = Join-Path $ProjectDir "Package.appxmanifest"
[xml]$manifest = Get-Content $manifestPath
$publisher = $manifest.Package.Identity.Publisher      # e.g. CN=728BA5DB-...
$pkgVersion = $manifest.Package.Identity.Version        # e.g. 0.9.6.0
Write-Host "Publisher: $publisher   Version: $pkgVersion"

# --- Tool discovery ---
function Find-Tool([string]$name) {
  $hit = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter $name -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $hit) {
    $hit = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter $name -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
  }
  if (-not $hit) { throw "Could not locate $name." }
  return $hit.FullName
}
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = (& $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe") | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found via vswhere. Install Visual Studio with the MSBuild component." }
$signtool = Find-Tool "signtool.exe"
Write-Host "MSBuild:  $msbuild"
Write-Host "signtool: $signtool"

# --- 1. Ensure signing cert matches the Publisher subject ---
$pfx = Join-Path $keysDir "QuinSlate-Signing.pfx"
$cer = Join-Path $DistDir "QuinSlate-Signing.cer"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher } | Select-Object -First 1
if (-not $cert) {
  Write-Host "Creating self-signed cert for $publisher"
  $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
    -FriendlyName "QuinSlate Sideload Signing" -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}
$sec = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $sec | Out-Null
Export-Certificate  -Cert $cert -FilePath $cer | Out-Null

# --- 2. Clean so every architecture recompiles from current source ---
Write-Host "Cleaning bin/obj..."
Remove-Item (Join-Path $ProjectDir "bin"), (Join-Path $ProjectDir "obj") -Recurse -Force -ErrorAction SilentlyContinue

# --- 3. Build + package ---
# Why these flags:
#   /restore (the SWITCH, not /t:Restore,Build) runs restore as a separate phase and
#     reloads targets. Restoring and building in ONE target list breaks the WinUI XAML
#     source generator (InitializeComponent / generated fields go missing -> CS0103/CS5001).
#   GenerateAppxPackageOnBuild=true actually emits the bundle; a plain Build won't.
#   AppxBundle=Always + AppxBundlePlatforms=x86|x64|arm64 -> one bundle that runs anywhere.
#   UapAppxPackageBuildMode=SideloadOnly -> .msixbundle for sideloading (not Store upload).
#   AppxPackageSigningEnabled=false -> we sign with signtool afterwards (see step 4).
$pkgDir = Join-Path $DistDir "packages\\"
Write-Host "Building (clean, all 3 arches self-contained R2R; this takes several minutes)..."
& $msbuild $ProjectPath `
  /p:Configuration=Release /p:Platform=x64 `
  "/p:AppxBundlePlatforms=x86|x64|arm64" `
  /p:AppxBundle=Always /p:GenerateAppxPackageOnBuild=true `
  /p:UapAppxPackageBuildMode=SideloadOnly `
  /p:AppxPackageSigningEnabled=false /p:GenerateTemporaryStoreCertificate=false `
  "/p:AppxPackageDir=$pkgDir" `
  /restore /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE." }

$bundle = Get-ChildItem (Join-Path $DistDir "packages") -Recurse -Filter *.msixbundle |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $bundle) { throw "No .msixbundle produced." }
Write-Host "Built: $($bundle.FullName)"

# --- 4. Sign with the publisher-matching cert ---
& $signtool sign /fd SHA256 /a /f $pfx /p $CertPassword /tr "http://timestamp.digicert.com" /td SHA256 $bundle.FullName
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }

# --- 5. Verify the actual bits before staging ---
$verifyArgs = @("-File", (Join-Path $scriptDir "verify_bundle.ps1"), "-Bundle", $bundle.FullName)
if ($Marker) { $verifyArgs += @("-Marker", $Marker) }
& powershell @verifyArgs
if ($LASTEXITCODE -ne 0) { throw "Verification failed - bundle contains stale/wrong bits. Not staging." }

# --- 6. Stage deliverables ---
$finalName = "QuinSlate_${pkgVersion}_x86_x64_arm64.msixbundle"
$finalPath = Join-Path $DistDir $finalName
Copy-Item $bundle.FullName -Destination $finalPath -Force

$nl = [Environment]::NewLine
$lines = @(
  "# QuinSlate $pkgVersion - Sideload Install",
  "",
  "Self-signed build for personal testing. Do these on the **target laptop**.",
  "",
  "## Files",
  "- QuinSlate-Signing.cer  - public signing cert (safe to share)",
  "- $finalName  - the app (covers x86/x64/arm64)",
  "- Do NOT copy the _private-keys-do-not-share folder - that's the private key.",
  "",
  "## Steps",
  "1. Trust the cert (one-time, admin PowerShell):",
  "   Import-Certificate -FilePath .\QuinSlate-Signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople",
  "2. If an older QuinSlate is installed, uninstall it first. If this build keeps the",
  "   same version number, Windows will NOT replace an existing install of that version:",
  "   Get-AppxPackage *QuinSlate* | Remove-AppxPackage",
  "3. Install: double-click the .msixbundle, or",
  "   Add-AppxPackage -Path .\$finalName"
)
Set-Content -Path (Join-Path $DistDir "INSTALL.md") -Value ($lines -join $nl) -Encoding utf8

Write-Host ""
Write-Host "DONE. Shareable deliverables in $DistDir :" -ForegroundColor Green
Write-Host "  $finalName"
Write-Host "  QuinSlate-Signing.cer"
Write-Host "  INSTALL.md"
Write-Host "Private key kept in $keysDir (do not share)."
