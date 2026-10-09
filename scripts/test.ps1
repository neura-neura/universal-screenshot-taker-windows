param([switch]$Interactive)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $taskRoot
try {
    & "$PSScriptRoot/prepare.ps1"
    dotnet run --project tests/UniversalCapture.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core validation failed' }
    if ($Interactive) {
        dotnet run --project tests/UniversalCapture.Tests -c Release -- --interactive
        if ($LASTEXITCODE -ne 0) { throw 'Interactive capture validation failed' }
    }
} finally { Pop-Location }
