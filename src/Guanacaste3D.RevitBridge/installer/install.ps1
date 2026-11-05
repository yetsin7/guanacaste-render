<#
.SYNOPSIS
Installs (or removes) the Guanacaste 3D add-in for every Revit version on this computer that the package supports.

.DESCRIPTION
Per user, without administrator rights: each RevitYYYY folder of the package is copied to
%APPDATA%\Autodesk\Revit\Addins\YYYY\Guanacaste3D and a manifest is written next to it. Revit must be closed while
the add-in is installed or removed. The first time Revit starts with the add-in it asks whether to trust it: choose
"Always load".
#>
[CmdletBinding()]
param([switch]$Uninstall, [switch]$EnableThumbnails)

$ErrorActionPreference = 'Stop'
$package = $PSScriptRoot
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
$done = @()
if (-not $Uninstall) {
    if ($EnableThumbnails) {
        & (Join-Path $package 'ensure-desktop-runtime.ps1') -PackageDirectory $package
        & (Join-Path $package 'register-thumbnails.ps1') -ProviderDirectory (Join-Path $package 'thumbnail-handler') -AllowInProcess
    }
    else {
        # Never enable in-process Explorer thumbnail parsing during a normal Revit add-in installation.
        & (Join-Path $package 'register-thumbnails.ps1') -Unregister
        Write-Host 'Explorer thumbnails disabled by default. Use -EnableThumbnails to opt in.'
    }
}

foreach ($source in Get-ChildItem $package -Directory -Filter 'Revit20*') {
    $year = $source.Name.Substring(5)
    if ($year -notmatch '^20[0-9]{2}$') { continue }
    $revit = Join-Path $env:ProgramFiles "Autodesk\Revit $year\Revit.exe"
    $addins = Join-Path $addinsRoot $year
    $target = Join-Path $addins 'Guanacaste3D'
    $manifest = Join-Path $addins 'Guanacaste3D.RevitBridge.addin'
    if ($Uninstall) {
        if (Test-Path $manifest) { Remove-Item $manifest -Force }
        $resolvedTarget = [IO.Path]::GetFullPath($target)
        if (-not $resolvedTarget.StartsWith([IO.Path]::GetFullPath($addinsRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The add-in directory is outside the expected user installation root.'
        }
        if (Test-Path -LiteralPath $resolvedTarget) { Remove-Item -LiteralPath $resolvedTarget -Recurse -Force }
        $done += "Revit $year"
        continue
    }
    if (-not (Test-Path $revit)) { continue }
    # A new version directory avoids replacing a DLL that a running Revit instance still owns.
    $fingerprint = (Get-FileHash -LiteralPath (Join-Path $source.FullName 'Guanacaste3D.RevitBridge.dll') -Algorithm SHA256).Hash.Substring(0, 20)
    $target = Join-Path $target $fingerprint
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($assemblyFile in Get-ChildItem -LiteralPath $source.FullName -File) {
        $installedFile = Join-Path $target $assemblyFile.Name
        if (-not (Test-Path -LiteralPath $installedFile)) { Copy-Item -LiteralPath $assemblyFile.FullName -Destination $installedFile }
    }
    $assembly = [System.Security.SecurityElement]::Escape((Join-Path $target 'Guanacaste3D.RevitBridge.dll'))
    @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Guanacaste 3D</Name>
    <Assembly>$assembly</Assembly>
    <AddInId>5B0C7F3E-2D4A-4E61-9C83-6A1F0B2E7D45</AddInId>
    <FullClassName>Guanacaste3D.RevitBridge.BridgeApplication</FullClassName>
    <VendorId>GUANACASTE3D</VendorId>
    <VendorDescription>Guanacaste 3D</VendorDescription>
  </AddIn>
</RevitAddIns>
"@ | Set-Content -Path $manifest -Encoding UTF8
    $done += "Revit $year"
}

if ($done.Count -eq 0) {
    Write-Host 'No installed Revit version matches the versions included in this package.' -ForegroundColor Yellow
    exit 1
}
if ($Uninstall) {
    Write-Host ("Removed the Guanacaste 3D add-in from: " + ($done -join ', ')) -ForegroundColor Green
} else {
    Write-Host ("Installed the Guanacaste 3D add-in for: " + ($done -join ', ')) -ForegroundColor Green
    Write-Host 'Restart Revit to load this version. The Guanacaste 3D tab contains the export commands.'
}
