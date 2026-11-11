param([switch]$IncludeVideo)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
$previous = $env:G3D_ONLY
try {
    dotnet build guanacaste-render.csproj -c Release -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }
    if (Get-Command cargo -ErrorAction SilentlyContinue) {
        cargo test --release --manifest-path engine/Cargo.toml
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas del motor Rust.' }
    } else {
        Write-Warning 'Rust no está instalado: se omiten las pruebas de engine/.'
    }
    # Keep render suites sequential: concurrent ray tracing can legitimately trigger the memory watchdog.
    dotnet run --project tests/GuanacasteRender.Checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas del motor.' }
    $env:G3D_ONLY = 'studio'
    dotnet run --project tests/desktop-interaction-checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas del estudio.' }
    $env:G3D_ONLY = 'model-update'
    dotnet run --no-build --project tests/desktop-interaction-checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de actualización del modelo.' }
    $env:G3D_ONLY = 'render-recovery'
    dotnet run --no-build --project tests/desktop-interaction-checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de recuperación.' }
    if ($IncludeVideo) {
        $env:G3D_ONLY = 'render'
        dotnet run --no-build --project tests/desktop-interaction-checks -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de foto y vídeo.' }
    }
} finally {
    $env:G3D_ONLY = $previous
    Pop-Location
}
