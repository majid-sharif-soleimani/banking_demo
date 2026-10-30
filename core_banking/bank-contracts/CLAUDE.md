# bank-contracts

Shared packages for every service: contracts (`Bank.Contracts`), and from slice 1.2 on `Bank.Common`, `Bank.Messaging`, `Bank.Messaging.Kafka`, `Bank.Testing`. `tools/Bank.Contracts.SchemaTool` is a developer tool and is never packed. Not a service: owns no tables and publishes no events.

## Contracts

- The C# records are the source of truth (ADR 0003). Each event is a `sealed record` in `src/Bank.Contracts/Events` (namespace `Bank.Contracts.Events`, one flat folder for all services, ADR 0005) implementing `IIntegrationEvent`, with `[Description]` on the type and `public const string SchemaUri = "iglu:se.bank/<event>/jsonschema/<version>"`, its contract version.
- Field descriptions and constraints are attributes on the record: `[property: Description]`, `[property: RegularExpression]`, `[property: MinLength]`, `[property: Range]`. The common fields take theirs from `IIntegrationEvent`.
- `tools/Bank.Contracts.SchemaTool` generates each schema with `JsonSchemaExporter` (`ContractSchemaGenerator`) and writes the snapshots (ADR 0004). The snapshots in `schemas/se.bank/<event>/jsonschema/<version>.json` are committed and embedded in `Bank.Contracts`.
- The tool writes, the tests only check. `dotnet run --project tools/Bank.Contracts.SchemaTool -- write` writes missing snapshots and never overwrites one; `-- check` only compares. Then `dotnet test`. To add or change an event use `/new-event`.
- Versions: only ADDITION (`1-0-0` → `1-0-1`, new optional fields) or MODEL (`2-0-0`, anything else). REVISION is not used. Never edit or regenerate a published snapshot; bump `SchemaUri` instead.
- JSON for event payloads always uses `ContractJson.Options` (camelCase, strict numbers, required and non-nullable fields enforced on read). `Money` serializes as `{ "amount": "1234.50", "currency": "SEK" }`.

## Messaging (Bank.Messaging, ADRs 0006–0010)

- A service registers `AddNpgsqlConnectionFactory(connectionString)` for its own role and schema, an `IMessageTransport` (Kafka from slice 1.3), then `AddBankMessaging(o => o.ServiceName = "...")`, `AddSqlMigrations(typeof(Program).Assembly, "<service>")` and one `AddEventHandler<TEvent, THandler>()` per handler.
- Only `NpgsqlConnectionFactory` names Npgsql (ADR 0010). Everything else uses `IDbConnectionFactory`, `IDbConnection` and `IDbTransaction`; for async begin, commit, rollback and dispose use `DbAsyncExtensions` (never `Task.Run`). Test code may use Npgsql where the test is about PostgreSQL itself.
- `DbSession` (`Bank.Messaging.Data`, scoped) is the one connection and transaction of a request or a handled message. Features call `BeginAsync`, pass `session.Transaction` to every Dapper write, publish, then `CommitAsync`. `PublishAsync` without a transaction throws.
- The pipeline gives each handler its own scope and transaction: inbox row `(message_id, handler_name)`, the handler's effect and its outgoing events commit together. Handlers never begin or commit.
- SQL is unqualified; the schema comes from the role's search path. Tables are snake_case; `AddBankMessaging` sets Dapper's `MatchNamesWithUnderscores`.
- Migrations: `.sql` files embedded with `LogicalName="migrations/%(Filename)%(Extension)"`, named `0001_...`, applied in one transaction before hosted services start. Never edit an applied script (the checksum check stops startup); add a new one.

## Gotchas

- The `schemas` resource names use the build machine's path separator; always read them through `ContractSchemas`, never by raw resource name.
- A contract property of type `decimal`, `double` or `float` fails generation on purpose: use `Money`.
- A type with a custom `JsonConverter` (such as `Money`) must implement `IJsonSchemaProvider` (`static abstract JsonObject JsonSchema()`), describing what its converter writes, next to the converter. The generator uses it for any type that implements it, and fails for a converter type that does not.
- A validation attribute or CLR type the generator does not map fails generation; add the mapping in `tools/.../ContractSchemaGenerator.cs`. Nested records are not supported.
- A field with a `pattern` needs a matching sample in `tests/.../Support/SampleFactory.cs`.
- The handler class name is part of the inbox key: renaming a handler makes it treat old messages as new. Two handlers with the same class name in one service fail at startup.
- Migration scripts must be able to run inside a transaction (no `CREATE INDEX CONCURRENTLY`).
- `Bank.Messaging.IntegrationTests` needs Docker. Each test creates its own role and schema in one shared PostgreSQL container.

## Commands

```bash
dotnet build && dotnet test
dotnet test tests/Bank.Messaging.IntegrationTests   # outbox, inbox and migrations against PostgreSQL (Docker)
dotnet run --project tools/Bank.Contracts.SchemaTool -- write   # writes missing schema snapshots
dotnet pack -c Release -o ../.local-feed     # then bump the version in each consumer
```
