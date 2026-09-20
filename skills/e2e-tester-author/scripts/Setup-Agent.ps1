param(
    [Parameter(Mandatory=$true)][string]$McpExecutable,
    [string]$BaseUrl = 'https://api.example.test',
    [string]$ServerName = 'e2e-tester',
    [string]$AgentDirectory = '',
    [string]$SkillDirectory = '',
    [switch]$ConfigureCodex
)
$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $McpExecutable).Path
$uri = [uri]$BaseUrl
if (!$uri.IsAbsoluteUri -or $uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw 'Use an explicit HTTP(S) API base URL without credentials.' }
if ($ServerName -notmatch '^[a-z][a-z0-9-]*$') { throw 'Use a simple MCP server name.' }
if (!$AgentDirectory) { $AgentDirectory = Join-Path $env:LOCALAPPDATA 'E2ETester/Agent' }
if (!$SkillDirectory) {
    $skillRoot = if ($env:CODEX_HOME) { Join-Path $env:CODEX_HOME 'skills' } else { Join-Path $env:USERPROFILE '.codex/skills' }
    $SkillDirectory = Join-Path $skillRoot 'e2e-tester-author'
}
$AgentDirectory = [IO.Path]::GetFullPath($AgentDirectory)
$SkillDirectory = [IO.Path]::GetFullPath($SkillDirectory)
New-Item -ItemType Directory -Path $AgentDirectory -Force | Out-Null
$binaryDirectory = Join-Path $AgentDirectory 'bin'
New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
$installedExecutable = Join-Path $binaryDirectory 'E2ETester.Mcp.exe'
if ($executable -ne $installedExecutable) { Copy-Item -LiteralPath $executable -Destination $installedExecutable -Force }
$config = Join-Path $AgentDirectory 'mcp-config.local.json'
if (!(Test-Path -LiteralPath $config)) {
    [ordered]@{
        workspace = (Join-Path $AgentDirectory 'workspace')
        allowedBaseUrls = @($BaseUrl)
        allowHttp = $true
        allowAuthoring = $true
        allowSavedQueries = $false
        allowInteractiveLogin = $false
        savedQueries = @()
        identities = @(@{ alias='cloud-user'; kind='bearer'; secretEnvironmentVariable='E2ETESTER_CLOUD_TOKEN'; headerName='Authorization'; prefix='' })
        databases = @()
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $config -Encoding utf8
} else { Write-Output 'Preserved existing MCP configuration and permissions.' }
$skillSource = Split-Path $PSScriptRoot -Parent
if ([IO.Path]::GetFullPath($skillSource) -ne $SkillDirectory) {
    if (Test-Path -LiteralPath $SkillDirectory) {
        $backup = Join-Path $AgentDirectory ('skill-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Copy-Item -LiteralPath $SkillDirectory -Destination $backup -Recurse
    }
    New-Item -ItemType Directory -Path $SkillDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $skillSource | Copy-Item -Destination $SkillDirectory -Recurse -Force
}
$client = @{ mcpServers = @{ $ServerName = @{ command=$installedExecutable; args=@('--config',$config) } } }
$client | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $AgentDirectory 'mcp-client.json') -Encoding utf8
if ($ConfigureCodex) {
    if (!(Get-Command codex -ErrorAction SilentlyContinue)) { throw 'Codex CLI is not on PATH. Use the generated mcp-client.json with your agent.' }
    & codex mcp add $ServerName -- $installedExecutable --config $config
    if ($LASTEXITCODE -ne 0) { throw 'Codex MCP registration failed.' }
    $codexDirectory = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
    $codexConfig = Join-Path $codexDirectory 'config.toml'
    $configuration = [IO.File]::ReadAllText($codexConfig)
    $pattern = '(?ms)^\[mcp_servers\.' + [regex]::Escape($ServerName) + '\][ \t]*\r?\n(?<body>.*?)(?=^\[|\z)'
    $section = [regex]::Match($configuration, $pattern)
    if (!$section.Success) { throw 'Could not locate the registered MCP section to enable token forwarding.' }
    if ($section.Groups['body'].Value -notmatch '(?m)^env_vars\s*=') {
        $offset = $section.Groups['body'].Index
        $configuration = $configuration.Insert($offset, "env_vars = [`"E2ETESTER_CLOUD_TOKEN`"]`n")
        [IO.File]::WriteAllText($codexConfig, $configuration, [Text.UTF8Encoding]::new($false))
    }
}
Write-Output "Skill: $SkillDirectory"
Write-Output "MCP config: $config"
Write-Output 'No API request was sent. Supply E2ETESTER_CLOUD_TOKEN in the agent process environment for authenticated execution; never paste it into chat or the package.'
