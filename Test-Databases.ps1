[CmdletBinding()]
param([switch]$MariaDb)
$ErrorActionPreference = 'Stop'
$project = 'e2etester-tests-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$composeFile = Join-Path $PSScriptRoot 'infra/database-tests/compose.yaml'
$names = @('E2E_DATABASE_PASSWORD', 'E2E_MYSQL_IMAGE', 'E2E_SQLSERVER_PORT', 'E2E_MYSQL_PORT', 'E2E_POSTGRES_PORT')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:E2E_MYSQL_IMAGE = if ($MariaDb) { 'mariadb:11.4' } else { 'mysql:8.0' }
    $env:E2E_DATABASE_PASSWORD = 'E2e!' + [Guid]::NewGuid().ToString('N')
    & docker compose --progress quiet -p $project -f $composeFile up -d
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the isolated database containers.' }
    $services = @(@('sqlserver', '1433', 'E2E_SQLSERVER_PORT'), @('mysql', '3306', 'E2E_MYSQL_PORT'), @('postgres', '5432', 'E2E_POSTGRES_PORT'))
    foreach ($service in $services) {
        $binding = & docker compose -p $project -f $composeFile port $service[0] $service[1]
        if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Could not resolve an isolated database port.' }
        [Environment]::SetEnvironmentVariable($service[2], $Matches[1], 'Process')
    }
    & dotnet run --project (Join-Path $PSScriptRoot 'tests/E2ETester.IntegrationTests') --configuration Release -- --live-databases
    if ($LASTEXITCODE -ne 0) { throw 'Database integration checks failed.' }
}
finally {
    # The unique project owns only disposable test containers and their anonymous volumes.
    & docker compose -p $project -f $composeFile down --volumes --remove-orphans
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
