# Builds the release folder and zip: artifacts\PassDPI-<version>-win-x64.zip
# Usage: pwsh build/publish.ps1 [-Version 0.1.0]
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$props = @()
if ($Version) { $props += "-p:Version=$Version" } else {
    $Version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
}

$out = Join-Path $root "artifacts\PassDPI-win-x64"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish src/PassDpi.Service -c Release -o $out -nologo @props
if ($LASTEXITCODE) { throw "service publish failed" }
dotnet publish src/PassDpi.App -c Release -o $out -nologo @props
if ($LASTEXITCODE) { throw "app publish failed" }

# Keep only what users need.
Get-ChildItem $out -Filter *.pdb | Remove-Item
Copy-Item LICENSE, THIRD_PARTY_NOTICES.md, README.md, README.tr.md $out

$zip = Join-Path $root "artifacts\PassDPI-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"$hash  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ascii

Get-ChildItem $out | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize
"Created $zip"
"SHA256 $hash"
