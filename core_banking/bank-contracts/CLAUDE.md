# bank-contracts

Shared packages for every service: `Bank.Contracts`, `Bank.Common`, `Bank.Toolkit.Data`, `Bank.Messaging`, `Bank.Messaging.Kafka`, `Bank.Testing`, all packed to `../.local-feed`. Data access is EF Core (workspace ADR 0002). `tools/Bank.Contracts.SchemaTool` is a developer tool and is never packed. Not a service: owns no tables and publishes no events.

## Contracts

- The C# records are the source of truth (ADR 0003). Each event is a `sealed record` in `src/Bank.Contracts/Events` (namespace `Bank.Contracts.Events`, one flat folder for all services, ADR 0005) implementing `IIntegrationEvent`, with `[Description]` on the type and `public const string SchemaUri = "iglu:se.bank/<event>/jsonschema/<version>"`, its contract version.
- Field descriptions and constraints are attributes on the record: `[property: Description]`, `[property: RegularExpression]`, `[property: MinLength]`, `[property: Range]`. The common fields take theirs from `IIntegrationEvent`.
- `tools/Bank.Contracts.SchemaTool` generates each schema with `JsonSchemaExporter` (`ContractSchemaGenerator`) and writes the snapshots (ADR 0004). The snapshots in `schemas/se.bank/<event>/jsonschema/<version>.json` are committed and embedded in `Bank.Contracts`.
- The tool writes, the tests only check. `dotnet run --project tools/Bank.Contracts.SchemaTool -- write` writes missing snapshots and never overwrites one; `-- check` only compares. Then `dotnet test`. To add or change an event use `/new-event`.
- Versions: only ADDITION (`1-0-0` → `1-0-1`, new optional fields) or MODEL (`2-0-0`, anything else). REVISION is not used. Never edit or regenerate a published snapshot; bump `SchemaUri` instead.
- JSON for event payloads always uses `ContractJson.Options` (camelCase, strict numbers, required and non-nullable fields enforced on read). `Money` serializes as `{ "amount": "1234.50", "currency": "SEK" }`.

## Where shared code goes (ADR 0011)

- `Bank.Common`: the bank's shared business vocabulary that business code talks to directly (`IBusinessClock`, `MoneyRounding`, `Result<T>`, `PerformedBy`, external-service interfaces). No I/O, no external packages.
- `Bank.Toolkit.<Area>`: technical building blocks unrelated to banking. No reference to any `Bank.*` package, no business rules. Only code that two real projects need. One package per area, created with its first real content. Today: `Bank.Toolkit.Data`.

## Database access (Bank.Toolkit.Data, EF Core, ADR 0012)

- A service has one `DbContext`. `AddNpgsqlDbContext<TContext>(connectionString)` registers it (scoped) on Npgsql with snake_case names, and the same instance as `DbContext` for the shared code. It is the only place that names the provider; design-time factories call `UseNpgsqlWithBankConventions` so migrations match.
- `AddMigrationsOnStartup<TContext>()` applies the EF Core migrations before any hosted service starts; EF Core's migration lock makes concurrent starts safe. Create migrations with `dotnet ef migrations add <Name> --output-dir Data/Migrations` (local tool, `dotnet tool restore`).
- Table names are explicit and singular (`ToTable("loan")`). The schema comes from the role's search path; the model names no schema.
- Raw SQL only where LINQ cannot express it, with `FromSql`/`ExecuteSqlAsync` and interpolated parameters (never concatenation).

## Messaging (Bank.Messaging, ADRs 0006, 0007, 0012)

- A service registers `AddNpgsqlDbContext<TContext>`, `AddMigrationsOnStartup<TContext>`, an `IMessageTransport` (`AddKafkaTransport`), `AddBankMessaging(o => o.ServiceName = "...")` and one `AddEventHandler<TEvent, THandler>()` per handler. Its `OnModelCreating` calls `modelBuilder.AddBankMessaging()`, so its migrations create `outbox_message` and `inbox_message`.
- A feature changes its entities, calls `PublishAsync` (which only tracks an outbox row), then `SaveChangesAsync` once: state change and event commit together, or neither is written.
- The pipeline gives each handler its own scope, context and transaction: inbox row first (raw `ON CONFLICT DO NOTHING`), then the handler, then `SaveChangesAsync` and commit. Handlers change the context and publish, and never call `SaveChangesAsync`.
- The dispatcher uses its own scope per poll and `FromSql(... FOR UPDATE SKIP LOCKED)`.

## Kafka transport (Bank.Messaging.Kafka, ADRs 0013, 0014, 0015)

- The only project that references Confluent.Kafka. A service calls `AddKafkaTransport(builder.Configuration)` next to `AddBankMessaging`. The broker address comes from `ConnectionStrings:kafka` (Aspire sets it); `Kafka:SubscribeTo` lists the **service names** it reads from (`["operations"]`, not topic names).
- Topic `bank.<service>.events`, key = entity id, value = event JSON, headers `message-id`, `schema-uri`, `performed-by`, `occurred-on`. Consumer group `bank.<service>`. `KafkaTopicInitializer` creates the own and subscribed topics plus their `.dlt` topics at startup.
- A failing message is retried in place (blocks its partition), four executions in total, then copied to `<topic>.dlt` with `dlt-*` headers. Permanent failures (`FailureClassification`) skip the retries. The offset is stored only after the pipeline returned or the DLT has the message.

## Gotchas

- The `schemas` resource names use the build machine's path separator; always read them through `ContractSchemas`, never by raw resource name.
- A contract property of type `decimal`, `double` or `float` fails generation on purpose: use `Money`.
- A type with a custom `JsonConverter` (such as `Money`) must implement `IJsonSchemaProvider` (`static abstract JsonObject JsonSchema()`), describing what its converter writes, next to the converter. The generator uses it for any type that implements it, and fails for a converter type that does not.
- A validation attribute or CLR type the generator does not map fails generation; add the mapping in `tools/.../ContractSchemaGenerator.cs`. Nested records are not supported.
- A field with a `pattern` needs a matching sample in `tests/.../Support/SampleFactory.cs`.
- The handler class name is part of the inbox key: renaming a handler makes it treat old messages as new. Two handlers with the same class name in one service fail at startup.
- The messaging column names are set explicitly in `ModelBuilderExtensions` because the dispatcher and the inbox use them in raw SQL; keep both in sync.
- `Version` is PostgreSQL's system column `xmin`: `SELECT *` leaves it out, so any `FromSql` on a mapped table must select `xmin` by name. Its migration creates no SQL.
- A business-key violation surfaces as `DbUpdateException` with an inner `PostgresException`.
- `Bank.Messaging.IntegrationTests` and `Bank.Toolkit.Data.IntegrationTests` need Docker. Each test creates its own role and schema in one shared PostgreSQL container. The Kafka tests share one broker per class and keep apart by unique service names (own topics and groups).
- `KafkaOptions.RetryDelays` starts empty and gets its default after binding: the configuration binder appends configured array items to existing ones instead of replacing them.
- Test code may read raw Kafka topics (`Support/KafkaTopicReader`) through the transitive Confluent.Kafka reference; no other `.csproj` may reference it directly. Nothing public in `Bank.Messaging.Kafka` may expose a Confluent type: `KafkaHeaders` is `internal`, visible only to the test projects.
- `CurrencyActivated.MinorUnits` is capped at 28 (the most a `decimal` holds), like `MoneyRounding`.

## Commands

```bash
dotnet build && dotnet test
dotnet test tests/Bank.Messaging.IntegrationTests     # outbox, inbox and Kafka transport (Docker)
dotnet test tests/Bank.Toolkit.Data.IntegrationTests  # DbContext registration and migrations on startup (Docker)
dotnet run --project tools/Bank.Contracts.SchemaTool -- write   # writes missing schema snapshots
dotnet pack -c Release -o ../.local-feed     # then bump the version in each consumer
```
