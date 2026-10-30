# Stage 1: Foundation

Goal: shared packages and the local platform exist, and an event can travel from one service's outbox through Kafka to another service's handler exactly once in effect.

Read first: `docs/implementation-decisions.md`, design plan sections 3, 4 (ADRs 04–11, 14–17), 6.

## Learning

This stage is large, so it runs as four slices with a checkpoint after each (see "Learning mode" in `docs/implementation-decisions.md`).

| Slice | Content | Concepts | Decisions for Majid (D1) | Majid writes (originally; reassigned, see below) |
| --- | --- | --- | --- | --- |
| 1.1 Contracts | `bank-contracts` folder, build props, `Money`, `IIntegrationEvent`, the five schemas, `Bank.Contracts.Tests` | Value objects, contract-first design, JSON Schema, SchemaVer, contract tests | How records and schemas stay in sync (reflection test, code generation from schema, or schema generation from records). ADR: schema/record sync | `Money` and its tests (arithmetic, currency mismatch, JSON shape); the ADR |
| 1.2 Outbox and inbox | `Bank.Common`, `Bank.Messaging` (EF Core, no Kafka; originally Dapper), `Bank.Testing` fixtures | The dual-write problem, transactional outbox, at-least-once delivery, idempotent consumers, inbox | How the dispatcher finds new outbox rows (polling, PostgreSQL LISTEN/NOTIFY, CDC with Debezium); the inbox key; the SQL migration runner and how `IEventPublisher` joins the feature's connection and transaction (both decided for Dapper, then superseded by EF Core, ADR 0012). ADRs: outbox dispatcher, inbox key, EF Core for the shared packages (0012, superseding migration runner and transaction sharing) | Integration tests "rollback publishes nothing" and "duplicate message applied once"; both ADRs |
| 1.3 Kafka transport | `Bank.Messaging.Kafka`: producer, consumer, headers, retry, DLT | Topics, partitions and keys, ordering, consumer groups, offset commits, poison messages, dead-letter topics | Retry strategy (in-consumer retry that blocks the partition, retry topics, or no retry). ADRs: message format and headers, retry and dead-letter | The backoff sequence function and its unit test; the retry and dead-letter ADR |
| 1.4 Platform | `bank-platform`: Aspire AppHost, init SQL, docker-compose, platform tests | Infrastructure as code, least-privilege database roles, service defaults, OpenTelemetry basics | none new | `db/init/001-roles-and-schemas.sql` and the test "each service role cannot read another schema"; ADR: one role and schema per service |

Change on 2026-10-30 (Majid): in stage 1, Claude writes everything in the "Majid writes" column too, including all ADRs, and writes a learning document per slice in `docs/learning/stage-1/`. Majid still makes the D1 decisions. Originally: Claude writes everything else in this stage, and the remaining ADRs (package layout). Before slice 1.2, Claude draws the full path of one event (service A transaction, outbox, dispatcher, Kafka, consumer, inbox, handler, service B transaction) as a sequence diagram, and Majid explains back where a crash at each step would leave the system.

## Folders created in this stage

Only `core_banking/bank-contracts` and `core_banking/bank-platform`, with their CI workflows `.github/workflows/bank-contracts.yml` and `bank-platform.yml`. Also create `core_banking/.local-feed/` (just a folder for packed packages).

## bank-contracts

```
bank-contracts/
  Directory.Build.props  Directory.Packages.props  nuget.config  CLAUDE.md
  docs/adr/
  schemas/se.bank/<event>/jsonschema/1-0-0.json
  src/Bank.Contracts/           Money, ContractJson; Events/ (IIntegrationEvent, all event records); Schema/ (SchemaKey, ContractSchemas); schemas as embedded resources
  src/Bank.Common/              interfaces and helpers listed in implementation-decisions.md
  src/Bank.Toolkit.Data/        AddNpgsqlDbContext, AddMigrationsOnStartup (added 2026-10-30, ADRs 0011 and 0012)
  src/Bank.Messaging/           publisher, handler, outbox and inbox entities, dispatcher, consumer pipeline (EF Core)
  src/Bank.Messaging.Kafka/     Kafka producer, consumer, DLT; only project referencing Confluent.Kafka
  src/Bank.Testing/             Testcontainers fixtures, FakeBusinessClock, Eventually helper, in-memory fakes
  tests/Bank.Contracts.Tests/
  tests/Bank.Common.Tests/
  tests/Bank.Toolkit.Data.IntegrationTests/
  tests/Bank.Messaging.UnitTests/
  tests/Bank.Messaging.IntegrationTests/
```

Public surface (names are fixed; signatures may be refined):

```csharp
public sealed record Money(decimal Amount, string Currency);                 // + - * comparison, CurrencyMismatchException
public interface IIntegrationEvent { Guid EventId { get; } DateOnly OccurredOn { get; } string PerformedBy { get; } }
public interface IBusinessClock { DateOnly Today { get; } }
public interface IEventPublisher { Task PublishAsync<T>(T @event, string key, CancellationToken ct) where T : IIntegrationEvent; }
public interface IEventHandler<in T> where T : IIntegrationEvent { Task HandleAsync(T @event, CancellationToken ct); }
// Bank.Messaging ships the outbox_message and inbox_message entities; modelBuilder.AddBankMessaging() adds them to the service's model and migrations
services.AddBankMessaging(options => ...);                // dispatcher + consumer pipeline; transaction sharing decided in slice 1.2
services.AddKafkaTransport(configuration);                // topic bank.<service>.events
```

Schemas created in this stage (Operations needs them in stage 2): `currency_activated`, `business_day_opened`, `day_closed`, `day_completed`. Fields:

| Event | Fields besides EventId, OccurredOn, PerformedBy |
| --- | --- |
| CurrencyActivated | Code, MinorUnits |
| BusinessDayOpened | Date |
| DayClosed | Date |
| DayCompleted | Date, Service |

Also a `test_event` schema used only by messaging integration tests. The `TestEvent` record and its schema ship in `Bank.Contracts` (every contract is generated and checked there); no service handles it.

Package versions: all five libraries (`Bank.Toolkit.Data` added by ADR 0011) plus `Bank.Testing` start at `0.1.0` and are packed to `../.local-feed`.

## bank-platform

```
bank-platform/
  src/Bank.AppHost/          Aspire: PostgreSQL (db "bank", init script), Kafka (KRaft), Kafka UI, Mailpit
  src/Bank.ServiceDefaults/  OpenTelemetry, health checks, resilience defaults
  db/init/001-roles-and-schemas.sql   one role + schema per service (6 services)
  tests/Bank.Platform.IntegrationTests/
  docker-compose.yml         same infrastructure without Aspire (used by stage 8); only infra, no services yet
```

Services are added to the AppHost in the stages that create them.

## Tests (minimum)

Bank.Contracts.Tests: every record matches its schema snapshot; schema path, `self` field and `SchemaUri` agree; SchemaVer compatibility check between consecutive versions; Money serialization shape; Money arithmetic and mismatch.

Bank.Messaging.UnitTests: backoff sequence; SchemaVer change classification.

Bank.Messaging.IntegrationTests (two schemas with two connections simulate two services): published event reaches the other side after commit; rollback publishes nothing; duplicate message applied once; failing handler retried three times then sent to DLT; same key handled in order; handler effect, inbox row and outgoing outbox row commit together or not at all.

Bank.Platform.IntegrationTests: AppHost starts PostgreSQL and Kafka healthy; each service role cannot read another schema.

## ADRs to write

In `bank-contracts/docs/adr`: package layout; Kafka message format and headers; outbox dispatcher polling; inbox key; retry and dead-letter; schema/record sync (generate schemas from records, ADR 0003). In `bank-platform/docs/adr`: one role and schema per service.

## Out of scope

Any service, the gateway, package publishing.

## Done when

```bash
cd core_banking/bank-contracts && dotnet build && dotnet test && dotnet pack -c Release -o ../.local-feed
cd core_banking/bank-platform  && dotnet build && dotnet test
grep -rl "Confluent.Kafka" core_banking --include=*.csproj    # prints only Bank.Messaging.Kafka.csproj
```
