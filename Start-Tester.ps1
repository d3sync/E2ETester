param([switch]$Demo)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build E2ETester.slnx --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $demoProcess = $null
    try {
        if ($Demo) {
            $demoProcess = Start-Process -FilePath (Join-Path $PSScriptRoot 'tests/E2ETester.IntegrationTests/bin/Release/net10.0/E2ETester.IntegrationTests.exe') -ArgumentList '--serve' -WindowStyle Hidden -PassThru
        }
        $desktop = Start-Process -FilePath (Join-Path $PSScriptRoot 'src/E2ETester.Desktop/bin/Release/net10.0-windows/E2ETester.Desktop.exe') -PassThru
        $desktop.WaitForExit()
    }
    finally {
        if ($null -ne $demoProcess -and -not $demoProcess.HasExited) { $demoProcess.Kill(); $demoProcess.WaitForExit() }
    }
}
finally { Pop-Location }
