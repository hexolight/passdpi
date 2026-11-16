# Builds the single-file release: artifacts\PassDPI-<version>-win-x64.exe (+ .sha256)
# The trimmed service exe is published first and embedded into PassDPI.exe; the installer extracts it.
# Usage: powershell -File build/publish.ps1 [-Version 0.1.0]
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$props = @()
if ($Version) { $props += "-p:Version=$Version" } else {
    $Version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
}

$art = Join-Path $root "artifacts"
$svcOut = Join-Path $art "obj\service"
$appOut = Join-Path $art "obj\app"
foreach ($d in $svcOut, $appOut) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }

dotnet publish src/PassDpi.Service -c Release -o $svcOut -nologo @props
if ($LASTEXITCODE) { throw "service publish failed" }
$svcExe = Join-Path $svcOut "PassDpi.Service.exe"

dotnet publish src/PassDpi.App -c Release -o $appOut -nologo "-p:EmbedServicePath=$svcExe" @props
if ($LASTEXITCODE) { throw "app publish failed" }

$exe = Join-Path $art "PassDPI-$Version-win-x64.exe"
Copy-Item (Join-Path $appOut "PassDPI.exe") $exe -Force
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
"$hash  $(Split-Path $exe -Leaf)" | Set-Content "$exe.sha256" -Encoding ascii

"Created $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 2)) MB, service $([math]::Round((Get-Item $svcExe).Length / 1MB, 2)) MB embedded)"
"SHA256 $hash"
