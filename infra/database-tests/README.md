# Disposable database integration tests

From the repository root, run `./Test-Databases.ps1` with Docker Desktop's Linux engine running and the .NET SDK installed. The first run downloads the SQL Server 2022 Developer, MySQL 8.0 and PostgreSQL 17 images. SQL Server runs with its container EULA accepted and Developer edition for these tests.

The launcher creates a uniquely named Compose project, generates a temporary password, publishes randomly allocated loopback-only ports, then runs the normal HTTP/SQLite suite plus live provider checks. Database readiness is checked with bounded connection retries. Only the test harness uses administrator access to create its fixture database and SELECT-only user; application queries run as that restricted user.

The script removes its own containers, network and anonymous volumes on success or failure and restores the process environment. Images stay cached. Existing containers and database data are not used. If the launcher process is forcibly terminated, its `e2etester-tests-<id>` project may need manual removal through Docker Desktop.

Coverage includes fixture binding, response-to-query parameters, typed values and typed NULLs, secret redaction, injection resistance, row/cell limits, duplicate columns, denied writes, active query cancellation/timeouts and connection recovery. A direct write attempt separately checks actual account permissions outside the application's read-only transaction.

The ordinary `dotnet run --project tests/E2ETester.IntegrationTests` command remains independent of Docker. `--live-databases` is reserved for the launcher's disposable database configuration; do not point it at an existing database.
