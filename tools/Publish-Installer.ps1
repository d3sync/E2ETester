param(
    [string]$Version = '',
    [string]$OutputDirectory = '',
    [string]$PackId = 'E2ETester',
    [string]$Shortcuts = 'Desktop,StartMenuRoot'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Version) { $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
python (Join-Path $root 'tools/check_publication.py')
if ($LASTEXITCODE -ne 0) { throw 'Publication audit failed.' }
if ($Version -notmatch '^\d+\.\d+\.\d+([.-][A-Za-z0-9.-]+)?$') { throw 'Use a semantic release version.' }
if ($PackId -notmatch '^[A-Za-z][A-Za-z0-9.]+$') { throw 'Invalid package ID.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/velopack' }
$publish = Join-Path $root ('artifacts/installer-stage/' + $Version + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $publish -Force | Out-Null
Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore pinned Velopack CLI.' }
    dotnet publish src/E2ETester.Desktop --configuration Release --runtime win-x64 --self-contained true --output $publish -p:PackagingRestore=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    $files = @(Get-ChildItem -LiteralPath $publish -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'E2ETester.Desktop.exe') { throw 'Expected one self-contained desktop executable.' }
    python (Join-Path $root 'tools/check_publication.py') --stage $publish
    if ($LASTEXITCODE -ne 0) { throw 'Public asset staging failed.' }
    dotnet publish src/E2ETester.Mcp --configuration Release --runtime win-x64 --self-contained true --output (Join-Path $publish 'mcp') -p:PackagingRestore=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw 'MCP companion publish failed.' }
    dotnet vpk pack --packId $PackId --packVersion $Version --packDir $publish --mainExe E2ETester.Desktop.exe --packTitle 'E2E Tester' --packAuthors 'd3sync and E2E Tester contributors' --icon (Join-Path $root 'assets/branding/e2e-tester.ico') --channel win --runtime win-x64 --outputDir $OutputDirectory --shortcuts $Shortcuts --delta None
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }
    Write-Output "Velopack installer, portable bundle and release feed: $OutputDirectory"
} finally { Pop-Location }
