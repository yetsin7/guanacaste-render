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
Remove-Item -LiteralPath $actual -Recurse -Force
Write-Output 'Guanacaste Render se desinstaló. Tus proyectos y renders permanecen en sus carpetas.'
