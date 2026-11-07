param([switch]$IncludeVideo)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
$previous = $env:G3D_ONLY
try {
    dotnet build guanacaste-render.csproj -c Release -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }
    # Keep render suites sequential: concurrent ray tracing can legitimately trigger the memory watchdog.
    dotnet run --project tests/GuanacasteRender.Checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas del motor.' }
    $env:G3D_ONLY = 'studio'
    dotnet run --project tests/desktop-interaction-checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas del estudio.' }
    if ($IncludeVideo) {
        $env:G3D_ONLY = 'render'
        dotnet run --no-build --project tests/desktop-interaction-checks -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de foto y vídeo.' }
    }
} finally {
    $env:G3D_ONLY = $previous
    Pop-Location
}
