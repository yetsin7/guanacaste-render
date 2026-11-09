$ErrorActionPreference = 'Stop'
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Guanacaste Render')).TrimEnd('\')
$actual = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
if ($actual -ne $expected) { throw 'Este desinstalador debe ejecutarse desde su carpeta de instalación.' }
if (Get-Process GuanacasteRender -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($actual + '\', [StringComparison]::OrdinalIgnoreCase) }) {
    throw 'Cierra Guanacaste Render antes de desinstalar.'
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Guanacaste Render.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GuanacasteRender' -ErrorAction SilentlyContinue
$renderExtension = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\.gr', $true)
if ($renderExtension) {
    if ($renderExtension.GetValue('') -eq 'GuanacasteRender.Project') { $renderExtension.DeleteValue('', $false) }
    $renderExtension.Dispose()
}
$openWith = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\.gr\OpenWithProgids', $true)
if ($openWith) {
    if ($openWith.GetValueNames() -contains 'GuanacasteRender.Project') {
        $openWith.DeleteValue('GuanacasteRender.Project', $false)
    }
    $removeOpenWith = $openWith.ValueCount -eq 0 -and $openWith.SubKeyCount -eq 0
    $openWith.Dispose()
    if ($removeOpenWith) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\Classes\.gr\OpenWithProgids', $false)
    }
}
$renderExtension = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\.gr')
if ($renderExtension) {
    $removeExtension = $renderExtension.ValueCount -eq 0 -and $renderExtension.SubKeyCount -eq 0
    $renderExtension.Dispose()
    if ($removeExtension) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\Classes\.gr', $false) }
}
Remove-Item -LiteralPath 'HKCU:\Software\Classes\GuanacasteRender.Project' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $actual -Recurse -Force
Write-Output 'Guanacaste Render se desinstaló. Tus proyectos y renders permanecen en sus carpetas.'
