param([string]$Source = (Join-Path $PSScriptRoot '..\release\win-x64'))
$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
if (!(Test-Path -LiteralPath (Join-Path $sourcePath 'GuanacasteRender.exe'))) { throw 'Primero publica la app en release/win-x64.' }
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Guanacaste Render'
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $sourcePath 'GuanacasteRender.exe')).ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versión de instalación no válida.' }
$destination = Join-Path $installRoot $version
$executable = Join-Path $destination 'GuanacasteRender.exe'
$running = Get-Process GuanacasteRender -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }
if ($running) { throw 'Cierra Guanacaste Render antes de actualizar esta versión.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $sourcePath '*') -Destination $destination -Recurse -Force
foreach ($binary in @('GuanacasteRender.exe', 'GuanacasteRender.dll', 'Guanacaste3D.Desktop.dll', 'Guanacaste3D.Core.dll')) {
    $expected = (Get-FileHash -LiteralPath (Join-Path $sourcePath $binary) -Algorithm SHA256).Hash
    $actual = (Get-FileHash -LiteralPath (Join-Path $destination $binary) -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw "La verificación de la instalación falló: $binary" }
}
# The Rust studio is installed beside the application when it has been built (cargo build --release in engine/).
$studioSource = Join-Path $PSScriptRoot '..\engine\target\release\GuanacasteStudio.exe'
if (Test-Path -LiteralPath $studioSource) {
    $studio = Join-Path $destination 'GuanacasteStudio.exe'
    if (Get-Process GuanacasteStudio -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $studio }) {
        throw 'Cierra Guanacaste Render Studio antes de actualizar esta versión.'
    }
    Copy-Item -LiteralPath $studioSource -Destination $studio -Force
    if ((Get-FileHash -LiteralPath $studio -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $studioSource -Algorithm SHA256).Hash) {
        throw 'La verificación de la instalación falló: GuanacasteStudio.exe'
    }
    $studioLink = (New-Object -ComObject WScript.Shell).CreateShortcut(
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'Guanacaste Render Studio.lnk'))
    $studioLink.TargetPath = $studio
    $studioLink.WorkingDirectory = $destination
    $studioLink.Description = 'Guanacaste Render Studio · Visor con trazado de rayos en GPU'
    $studioLink.Save()
    Write-Output "Instalado: $studio"
}
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Guanacaste Render.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $destination
$shortcut.Description = 'Guanacaste Render · Estudio de visualización'
$shortcut.Save()
$projectProgId = 'GuanacasteRender.Project'
$classesPath = 'Software\Classes\'
$projectClass = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($classesPath + $projectProgId)
$projectClass.SetValue('', 'Proyecto de Guanacaste Render', [Microsoft.Win32.RegistryValueKind]::String)
$projectClass.Dispose()
$iconClass = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($classesPath + $projectProgId + '\DefaultIcon')
$iconClass.SetValue('', $executable + ',0', [Microsoft.Win32.RegistryValueKind]::String)
$iconClass.Dispose()
$openCommand = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($classesPath + $projectProgId + '\shell\open\command')
$openCommand.SetValue('', '"' + $executable + '" "%1"', [Microsoft.Win32.RegistryValueKind]::String)
$openCommand.Dispose()
$renderExtension = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($classesPath + '.gr')
if (!$renderExtension.GetValue('')) { $renderExtension.SetValue('', $projectProgId, [Microsoft.Win32.RegistryValueKind]::String) }
$renderExtension.Dispose()
$openWith = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($classesPath + '.gr\OpenWithProgids')
$openWith.SetValue($projectProgId, '', [Microsoft.Win32.RegistryValueKind]::String)
$openWith.Dispose()
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $installRoot 'uninstall.ps1') -Force
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuanacasteRender'
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayName -Value 'Guanacaste Render' -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayVersion -Value $version -Force | Out-Null
New-ItemProperty -Path $key -Name Publisher -Value 'Guanacaste' -Force | Out-Null
New-ItemProperty -Path $key -Name InstallLocation -Value $destination -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayIcon -Value $executable -Force | Out-Null
$uninstall = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $installRoot 'uninstall.ps1') + '"'
New-ItemProperty -Path $key -Name UninstallString -Value $uninstall -Force | Out-Null
Write-Output "Instalado: $executable"
Write-Output 'Verificados con SHA256: ejecutable, aplicación, interfaz y motor.'
