# bank-platform

The local platform and its infrastructure, not a service: owns no tables and publishes no events. `src/Bank.AppHost` (Aspire 13.6) starts PostgreSQL (database `bank`), Kafka in KRaft mode with Kafka UI, Mailpit, and the services created so far (Operations since stage 2). `docker-compose.yml` runs the same infrastructure without Aspire (EC2 in stage 8). `src/Bank.ServiceDefaults` is what every service registers first (`AddServiceDefaults`, `MapDefaultEndpoints`). `tests/Bank.EndToEndTests` runs the five stage 8 scenarios through the gateway. `deploy/` holds the AWS side (ADRs 0002–0004): `docker-compose.aws.yml`, the CDK app in `deploy/cdk`, instance scripts and start/stop/backup scripts. Claude never runs `cdk deploy`, `aws` commands or image pushes.

## Database (ADR 0001)

- One login role and one schema per service, same name: `operations`, `customers`, `accounts`, `cards`, `lending`, `ledger`. Created by `db/init/001-roles-and-schemas.sql`; each role owns its schema, has it as search path, and has no rights anywhere else.
- Role passwords come only from the environment (`<SERVICE>_DB_PASSWORD`): AppHost secret parameters `<service>-db-password` (generated unless set in user secrets) or `.env` for Compose. Never in the script, the code or the compose file.
- A service gets its connection string from the AppHost resource `<service>-db` through `WithReference`; it connects as its role, never as `postgres`.
- The init script runs only on an empty data directory. The AppHost has no volume (fresh database every run); Compose has `postgres-data` (`docker compose down -v` to rerun the script; deletes all data).

## Adding a service (in the stage that creates it)

- In `AppHost.cs`: `builder.AddProject<Projects.Bank_X_Api>("x").WithReference(serviceDatabases["x"]).WithReference(kafka).WaitFor(...)`. The `<service>-db` connection strings already exist for all six services.
- Add a `ProjectReference` to the service's API project in `Bank.AppHost.csproj` (it generates `Projects.Bank_X_Api`), `.WithHttpHealthCheck("/health")`, a case in `InfrastructureHealthTests` and a migration check in `ServiceStartupTests`, and the service's `src/**` path to `.github/workflows/bank-platform.yml`.
- Services reference `Bank.ServiceDefaults` as a package from `../.local-feed` (`dotnet pack src/Bank.ServiceDefaults -c Release -o ../.local-feed`). The CI workflows pack it and `bank-contracts` from the same commit first.
- `Seq:ServerUrl` sends a service's logs to Seq as well, and `OTEL_RESOURCE_ATTRIBUTES: log_group=<group>` puts them in a group (ADR 0005). At home the agent's group is on; `banking-demo` is the next step.
- A new service name (not one of the six) needs: a block in the init script, `BankDatabase.Services`, `.env.example`, `docker-compose.yml` and `ServiceRoleIsolationTests.Services`.

## Gotchas

- `Aspire.Hosting.AppHost` comes with `Aspire.AppHost.Sdk`; do not add it to `Directory.Packages.props` (NU1009). Bump the SDK version in `Bank.AppHost.csproj` together with the Aspire packages.
- `ASPIRE010` is suppressed: we use `dotnet run`, and the SDK takes DCP and the dashboard from NuGet.
- Compose image tags are copied from the Aspire packages (`postgres:18.3`, `confluentinc/confluent-local:8.2.0`, `kafbat/kafka-ui:v1.5.0`, `axllent/mailpit:v1.31.2`). Update both when upgrading Aspire.
- Kafka from the host: Aspire gives a random port (see the dashboard or the `kafka` connection string); Compose uses `localhost:9092`, containers `kafka:9093`.
- Tests start the real AppHost once (`PlatformFixture`, assembly fixture); they need Docker.
- `identity` (workspace ADR 0007) has no database and no Kafka: it references and waits for `customers`, and the gateway waits for it. Every `/api` call in tests logs in first (`/api/identity/login`): platform tests as admin, end-to-end staff as admin and customers with the national id remembered at registration (`BankFixture.RememberNationalId`); a `session_*` 401 logs in again and repeats the request once.
- End-to-end scenarios start a fresh AppHost per test class (`BankFixture`) and run one after another (`xunit.runner.json`); every service's log lands in `bin/.../e2e-logs/<run>/`. Reads that depend on another service's events, or on a service's own business date (which follows Operations a moment later), go through `Eventually`.
- Money in the API is `{ "amount": "1234.50", "currency": "SEK" }` with the amount as a **string**; a number gives 500 in Development (the request body throws).
- `docker-compose.aws.yml` loads secrets only from optional `env/<container>.env` files written on the instance, so `docker compose config` stays valid without them. `deploy/env/` is gitignored.
- `cdk synth` (`npx aws-cdk@2.1145.0 synth` in `deploy/cdk`) needs Node but no AWS account; the stack names only a region.

## Commands

```bash
dotnet build && dotnet test                          # tests need Docker (platform about 1 minute, end-to-end about 5)
dotnet test tests/Bank.EndToEndTests                 # the five scenarios only
dotnet run --project src/Bank.AppHost                # dashboard URL is printed at start
cp .env.example .env && docker compose up -d --wait  # edit .env first
docker compose down -v                               # remove containers and data
```
