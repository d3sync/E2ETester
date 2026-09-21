param([string]$Version = '0.7.6')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
python (Join-Path $root 'tools/check_publication.py')
if ($LASTEXITCODE -ne 0) { throw 'Publication audit failed.' }
if ($Version -notmatch '^\d+\.\d+\.\d+([.-][A-Za-z0-9.-]+)?$') { throw 'Use a simple release version.' }
$releaseRoot = Join-Path $root 'artifacts/releases'
$stage = Join-Path $releaseRoot ('E2ETester-' + $Version + '-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $stage -ErrorAction Stop | Out-Null
foreach ($hostName in @('Desktop','Mcp')) {
    $project = Join-Path $root "src/E2ETester.$hostName/E2ETester.$hostName.csproj"
    $destination = Join-Path $stage $hostName.ToLowerInvariant()
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $destination -p:PackagingRestore=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "Publishing $hostName failed." }
    $publishedFiles = @(Get-ChildItem -LiteralPath $destination -Recurse -File)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne "E2ETester.$hostName.exe") {
        throw "Publishing $hostName did not produce exactly one executable. Check for unbundled dependencies."
    }
}
python (Join-Path $root 'tools/check_publication.py') --stage $stage
if ($LASTEXITCODE -ne 0) { throw 'Public asset staging failed.' }
$manifest = Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
[ordered]@{ version = $Version; platform = 'win-x64'; singleFile = $true; selfContained = $true; signed = $false; files = @($manifest) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'release-manifest.json') -Encoding utf8
$archive = $stage + '.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
Write-Output "Single-file executables (desktop and MCP), with documentation: $archive"
