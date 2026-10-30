# Stage 1 plan: Foundation (contracts, messaging, platform)

## Context

Stage 0 is Done (docs/progress.md). Stage 1 builds the shared packages (`bank-contracts`) and the local platform (`bank-platform`) so that an event can travel from one service's outbox through Kafka to another service's handler, exactly once in effect. Environment checked: running in `~/repos/banking_demo` on `<machine>` (no worktree).

**Working mode for stage 1 (Majid, 2026-10-30), replacing the stage file's "Majid writes" column for this stage only:**
- Claude writes everything: code, tests, SQL and all ADRs.
- Majid still makes every D1 decision. Claude presents 2–3 options with a recommendation and waits.
- For every slice, Claude writes a learning document, `docs/learning/stage-1/slice-1.N.md`, covering:
  - the concepts in plain terms, and what breaks without them
  - what was built, file by file
  - the patterns used and why
  - a diagram where it helps
  - the D2 choices
  - questions for Majid to check his understanding
- Slices and checkpoints stay as they are. Claude stops after each slice.
- Stages 2–8 keep their "Majid writes" items until Majid decides otherwise.

Approved by Majid on 2026-10-30.

## Slices

### 1.1 Contracts
- **Folders:**
  - `core_banking/bank-contracts/` with `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, ImplicitUsings), `Directory.Packages.props`, `nuget.config` (`../.local-feed` + nuget.org), `CLAUDE.md`, `docs/adr/` and a solution file
  - `core_banking/.local-feed/`
  - `.github/workflows/bank-contracts.yml` (restore, build, test; `paths:` limited to the folder; no publish)
- **Projects:** `src/Bank.Contracts`, `tests/Bank.Contracts.Tests` (xUnit v3, Shouldly).
- **Public surface:**
  - `Money(decimal Amount, string Currency)` with `+`, `-`, comparison, `Money * decimal`, `CurrencyMismatchException`, and a JSON converter that writes `{ "amount": "1234.50", "currency": "SEK" }`
  - `IIntegrationEvent { EventId, OccurredOn, PerformedBy }`
  - Records `CurrencyActivated(Code, MinorUnits)`, `BusinessDayOpened(Date)`, `DayClosed(Date)`, `DayCompleted(Date, Service)` and `TestEvent`, each with a `SchemaUri` constant
  - Schemas at `schemas/se.bank/<event>/jsonschema/1-0-0.json`, generated from the records, committed as snapshots and embedded as resources
- **Tests first:**
  - Money arithmetic, comparison, currency mismatch and JSON shape
  - Every record matches its schema snapshot, and the generator maps types and attributes correctly
  - Schema path, `self` and `SchemaUri` agree
  - Compatibility between consecutive versions (ADDITION or MODEL only)
- **ADRs:**
  - Package layout
  - Schema/record sync by reflection test (superseded)
  - Generate event schemas from records
- **D1:** schema/record sync. Decided: **reflection test**; changed the same day to **schema generation from records**, with committed snapshots.
- **Learning document:** `docs/learning/stage-1/slice-1.1.md`

### Before 1.2
Claude draws the sequence diagram of one event's full path, with a "crash at this step leaves…" table, in `docs/learning/stage-1/event-path.md`. Majid explains it back.

### 1.2 Outbox and inbox
- **Projects:**
  - `src/Bank.Common`: `IBusinessClock`, `MoneyRounding`, `IFileStorage`, `IEmailSender`, `IExchangeRateProvider`, `IAccountNumberGenerator`, `PerformedBy` helpers, `Result<T>`
  - `src/Bank.Messaging` (EF Core only): `IEventPublisher`, `IEventHandler<T>`, `AddOutboxAndInbox()`, `AddBankMessaging<TDbContext>()`, the outbox dispatcher, and the consumer pipeline with an in-process transport seam
  - `src/Bank.Testing`: PostgreSQL and Kafka Testcontainers fixtures, `FakeBusinessClock`, `Eventually`, in-memory fakes
  - `tests/Bank.Messaging.UnitTests`: SchemaVer change classification
  - `tests/Bank.Messaging.IntegrationTests`: two DbContexts in two schemas
- **Tests first:**
  - The published event reaches the other side after commit
  - Rollback publishes nothing
  - A duplicate message is applied once
  - The handler effect, the inbox row and the outgoing outbox row commit together or not at all
- **ADRs:** outbox dispatcher; inbox key.
- **D1 (already decided, Majid reconfirms):**
  - How the dispatcher finds new rows: polling with `FOR UPDATE SKIP LOCKED` every 200 ms, LISTEN/NOTIFY, or Debezium CDC
  - Inbox key: `(MessageId, HandlerName)` or MessageId only
- **Learning document:** `docs/learning/stage-1/slice-1.2.md`

### 1.3 Kafka transport
- **Project:** `src/Bank.Messaging.Kafka`, the only project that references Confluent.Kafka.
  - Producer to `bank.<service>.events`; the key is the entity id
  - Headers `message-id`, `schema-uri`, `performed-by`, `occurred-on`
  - Consumer group `bank.<service>`; handler chosen by schema URI, and an unknown MODEL version goes to the DLT
  - Retry, then `<topic>.dlt` and offset commit
  - `AddKafkaTransport(configuration)`
- **Tests first:**
  - Backoff sequence (200 ms, 1 s, 5 s)
  - A failing handler is retried three times, then sent to the DLT
  - Messages with the same key are handled in order
- **ADRs:** message format and headers; retry and dead-letter.
- **D1 (already decided, Majid reconfirms):** retry strategy. Options: in-consumer retry that blocks the partition, retry topics, or no retry.
- **Learning document:** `docs/learning/stage-1/slice-1.3.md`

### 1.4 Platform
- **Folder** `core_banking/bank-platform/`:
  - `src/Bank.AppHost`: PostgreSQL db `bank` with the init script, Kafka (KRaft), Kafka UI, Mailpit
  - `src/Bank.ServiceDefaults`: OpenTelemetry, health checks, resilience defaults
  - `db/init/001-roles-and-schemas.sql`
  - `docker-compose.yml` and `.env.example` (infrastructure only)
  - `tests/Bank.Platform.IntegrationTests`
  - `.github/workflows/bank-platform.yml`, `CLAUDE.md`, `docs/adr/`
- **Tests first:**
  - The AppHost starts PostgreSQL and Kafka healthy
  - Each service role cannot read another schema
- **ADR:** one role and schema per service.
- **D1:** none new. Credentials come only from AppHost user-secrets parameters or `.env`.
- **Learning document:** `docs/learning/stage-1/slice-1.4.md`

## ADRs (all written by Claude in stage 1)

| ADR | Folder |
| --- | --- |
| Package layout | bank-contracts/docs/adr |
| Schema/record sync by reflection test (0002, superseded by 0003) | bank-contracts/docs/adr |
| Generate event schemas from records (0003) | bank-contracts/docs/adr |
| Outbox dispatcher | bank-contracts/docs/adr |
| Inbox key | bank-contracts/docs/adr |
| Kafka message format and headers | bank-contracts/docs/adr |
| Retry and dead-letter | bank-contracts/docs/adr |
| One role and schema per service | bank-platform/docs/adr |

## Done when

```bash
cd core_banking/bank-contracts && dotnet build && dotnet test && dotnet pack -c Release -o ../.local-feed
cd core_banking/bank-platform  && dotnet build && dotnet test
grep -rl "Confluent.Kafka" core_banking --include=*.csproj    # prints only Bank.Messaging.Kafka.csproj
```

## Blocking questions

None. Non-blocking notes:
- Design plan ADR 10 says GitHub Packages. `docs/implementation-decisions.md`, which wins for implementation details, says the local feed for now. No change.
- "Every table has CreatedAt, PerformedBy, Version" will also apply to `outbox_message` and `inbox_message` unless Majid objects.
- `.gitignore` lacks `cdk.context.json` and `cdk.out/`. Add them in stage 8.

## Execution steps
1. Write `docs/stages/stage-1-plan.md` with this content.
2. In `docs/stages/stage-1.md`, change the "Majid writes" column to "none for stage 1; Claude writes all and documents each slice (Majid, 2026-10-30)".
3. Set stage 1 to "In progress" in `docs/progress.md` and create branch `stage-1-foundation`.
4. Slice 1.1:
   - Scaffold the folder and projects
   - Money tests, then `Money`
   - Contract tests, then schemas and records
   - Both ADRs
   - The learning document
5. `dotnet build` and `dotnet test`, check `git diff --cached` for secrets, commit, then the checkpoint. Stop.
6. Before each later slice: the design briefing with its D1 options. Wait for Majid.

## Verification
- After each slice: `dotnet build` and `dotnet test` in the touched folder, with the summary lines shown at the checkpoint.
- At the end: the full done-when commands.

## Decisions made
- 2026-10-30, slice 1.1 D1: schema/record sync by **reflection test**.
- 2026-10-30, slice 1.1 D1 changed by Majid: the C# records are the source of truth; schemas are generated with `JsonSchemaExporter` and committed as snapshots; only ADDITION (optional fields) or MODEL bumps. ADR 0003 supersedes ADR 0002.
- 2026-10-30: in stage 1, Claude writes everything and a learning document per slice. Majid keeps the D1 decisions. Stages 2–8 are unchanged.
