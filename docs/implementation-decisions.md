# Implementation Decisions (cross-cutting)

Decided by Majid on 2026-10-29. These settle the cross-cutting technical questions, and the Learning mode section below sets how Majid and Claude work together. They apply to every stage. If something here conflicts with `phase1-design-plan.md`, this file wins for implementation details and the plan wins for business rules.

## Learning mode

This project is built to learn solution and software engineering. Claude is a senior architect and pair programmer: it explains, offers options, writes the routine code and reviews Majid's code. Majid makes the design decisions and writes the parts listed under "Majid writes" in each stage file. (This section replaced the earlier autonomy policy on 2026-10-29.)

### Who decides

| Level | What | How |
| --- | --- | --- |
| D1, Majid decides | Anything that deserves an ADR: choosing between patterns, libraries or data shapes; trade-offs in consistency, reliability, performance or security; public interfaces of shared packages; anything listed under "Decisions for Majid" in the stage file | Claude presents 2 or 3 options. For each: what problem it solves, what it costs, when it would be the wrong choice. Claude labels its own recommendation and waits. Majid decides and writes the ADR; Claude critiques the ADR. |
| D2, Claude decides and explains | Naming, file layout that follows the conventions, test helper internals, small refactors, choices with no lasting consequence | Claude decides, continues, and lists these choices with one line each at the next slice checkpoint. When unsure between two reasonable options, pick the simpler one. |
| Already decided | Everything in this file and in the design plan | Not reopened. Claude explains it the first time it implements it. Majid can reopen any decision; then it becomes D1. When a stage file lists an already-decided item under "Decisions for Majid", Claude presents the options again with the current decision among them, and Majid confirms or changes it before writing the ADR. |

Claude always stops and asks when:

1. A business rule is missing or contradicts the design plan (interest, penalties, allocation, classification, payoff, accounting).
2. An action would publish outside this machine or cost money: creating GitHub repos, pushing, publishing packages, deploying to AWS.
3. An action would delete data or files Claude did not create in this stage.

### Who writes

- **Majid writes** the items listed under "Majid writes" in each stage file, test first. Claude does not write them. When Majid is stuck, Claude helps in increasing steps: a guiding question, then the concept, then pseudo-code, and real code only when Majid explicitly asks for it. When Majid is done, Claude reviews with `/review-me`.
- **Claude writes** everything else, in thin slices, test first for `Logic/`.

### Rhythm of a stage

1. `/start-stage N`: Claude writes the stage plan and gives a short design briefing (the concepts of the first slice and its D1 decisions). Majid decides.
2. For each slice: Claude builds its part and Majid builds his part. At the end of the slice there is a checkpoint: build and test evidence, what was built, the patterns used and why, the D2 choices made, and two short questions Majid can answer to check his understanding. Claude then stops until Majid says to continue.
3. `/close-stage N`: done-when commands, review, then `/retro` to write the learning log with Majid.

`/explain <topic>` can be used at any time for a deeper explanation of a pattern or a piece of code.

## Repository and packages

| Topic | Decision |
| --- | --- |
| When service folders are created | Each folder under `core_banking/` is created in the stage that first needs it (see each stage file). No empty skeletons ahead of time. |
| Git | Stage branch `stage-<n>-<name>`, merged into `master`. Majid pushes himself. |
| Package distribution | Local NuGet feed at `core_banking/.local-feed/` (created by `bank-contracts`). Every service folder has a `nuget.config` with this feed plus nuget.org. `dotnet pack -o ../.local-feed` publishes. GitHub Packages is configured later by Majid. |
| Package versions | SemVer. Start at `0.1.0`. Consumers reference an exact version. After changing `bank-contracts`, pack, then bump the version in each consumer. |
| Central package management | `Directory.Packages.props` in every service folder. `Directory.Build.props` sets `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`. |
| Secrets and personal data | Never in a tracked file. Local: `dotnet user-secrets` (services and Aspire AppHost) and a gitignored `.env` for Docker Compose, with a committed `.env.example`. AWS: AWS Secrets Manager, read through the EC2 IAM instance role; CDK creates the secret but never holds its value. CI: GitHub repository secrets. Code reads them only through `IConfiguration`. The `.gitignore` covers `.env`, `secrets/`, keys and local appsettings. Which .NET configuration provider loads Secrets Manager values is a D1 decision in the stage that first deploys to AWS. Details: `.claude/rules/secrets.md`. |
| CI | Each service folder gets `.github/workflows/<folder>.yml` (restore, build, test), limited to its folder with `paths:`. It must not publish anything. |

## Shared code (all in the `bank-contracts` folder)

| Package | Contains |
| --- | --- |
| `Bank.Contracts` | `Money`, `IIntegrationEvent`, all event records with `SchemaUri` constants, and the JSON Schemas generated from them as embedded resources |
| `Bank.Common` | The bank's shared business vocabulary: `IBusinessClock`, `MoneyRounding`, `IFileStorage`, `IEmailSender`, `IExchangeRateProvider`, `IAccountNumberGenerator` interfaces, `PerformedBy` helpers, a `Result<T>` type. No I/O, no external packages |
| `Bank.Toolkit.<Area>` | Technical building blocks unrelated to banking, one package per area, no reference to any `Bank.*` package (bank-contracts ADR 0011). Today `Bank.Toolkit.Data`: `AddNpgsqlDbContext` and `AddMigrationsOnStartup` for EF Core |
| `Bank.Messaging` | `IEventPublisher`, `IEventHandler<T>`, outbox and inbox entities (`modelBuilder.AddBankMessaging()`), dispatcher, consumer pipeline (EF Core, no Kafka) |
| `Bank.Messaging.Kafka` | The only project that references Confluent.Kafka |
| `Bank.Testing` | Test helpers: Testcontainers fixtures (PostgreSQL, Kafka), fake clock, polling `Eventually` helper, in-memory fakes for the interfaces in `Bank.Common` |

## Money

- `record Money(decimal Amount, string Currency)` in `Bank.Contracts`.
- Supports `+`, `-`, comparison and `Money * decimal`. Operations on two different currencies throw `CurrencyMismatchException`.
- No rounding inside `Money`. Rounding happens explicitly through `MoneyRounding.Round(money, minorUnits)` with `MidpointRounding.AwayFromZero`.
- JSON shape: `{ "amount": "1234.50", "currency": "SEK" }`. Amount is a string to avoid floating-point loss in other consumers.

## Time and the business date

- Every calendar day is a business day. No weekends or holidays in phase 1. Due dates are never shifted.
- Operations owns the business date. It publishes `BusinessDayOpened(date)` and `DayClosed(date)`.
- Each service keeps a local `business_day` table updated from those events. `IBusinessClock.Today` reads it. Tests use a fake clock.
- Day closing is triggered only by the API (`POST /business-days/current/close`), by tests, or by the import tool. No timers.
- Sequence: `DayClosed(D)` → Accounts and Lending run their end-of-day work for D → each publishes `DayCompleted(D, service)` → when Operations has both, it opens D+1 and publishes `BusinessDayOpened(D+1)`.
- Timestamps that are not business dates (`CreatedAt`, `SentAt`) use UTC `DateTimeOffset` from `TimeProvider`.

## Messaging details

- Kafka message key is the entity id, value is the event JSON. Headers: `message-id`, `schema-uri`, `performed-by`, `occurred-on` (bank-contracts ADR 0014).
- Consumers pick the handler by schema URI. Unknown MODEL versions go to the DLT; unknown ADDITION versions within a known MODEL are handled with the latest known record.
- Inbox key is `(MessageId, HandlerName)`, so two handlers in one service can process the same event.
- Outbox dispatcher: polling with `FOR UPDATE SKIP LOCKED`, interval configurable, default 200 ms.
- Retries happen inside the consumer: one first attempt plus three retries (backoff 200 ms, 1 s, 5 s, so four executions), then the message goes to `<topic>.dlt` and the offset is committed. Permanent failures (unknown MODEL, bad JSON, missing or malformed headers) go to the DLT without retries; a business-key violation is retried like any other failure. Blocking the partition during retries is accepted for this demo (confirmed by Majid in slice 1.3, bank-contracts ADR 0013).
- Consumer group per service: `bank.<service>`. Each service lists the services it reads from in `Kafka:SubscribeTo` (decided by Majid in slice 1.3, bank-contracts ADR 0015).

## Database

- One PostgreSQL database `bank`. One schema and one login role per service (`operations`, `customers`, `accounts`, `cards`, `lending`, `ledger`). Each role can only use its own schema. The init script lives in `bank-platform`.
- Data access is EF Core with the Npgsql provider (workspace ADR 0002, which supersedes the Dapper decision of ADR 0001). Each service has one `DbContext`, registered with `AddNpgsqlDbContext` from `Bank.Toolkit.Data`. Raw SQL only where LINQ has no equivalent, through `FromSql`/`ExecuteSqlAsync` with interpolated parameters.
- Schema changes are EF Core migrations per service (`Data/Migrations`, `dotnet ef migrations add`), applied at startup in development and by the AppHost with `AddMigrationsOnStartup`, before any hosted service starts (bank-contracts ADR 0012).
- Snake-case names through `EFCore.NamingConventions`; table names are set explicitly and singular (`ToTable("loan")`). The schema comes from the role's search path, so the model names no schema.
- Optimistic concurrency on `Version`: a `uint Version` property mapped to PostgreSQL `xmin` with `.IsRowVersion()`. EF adds it to every update's `WHERE` and throws `DbUpdateConcurrencyException` when no row matches.
- One `DbContext` per request or per handled message (scoped). `SaveChangesAsync` writes the feature's changes and the outbox rows from `IEventPublisher` in one transaction; a feature that needs several saves opens `Database.BeginTransactionAsync`. Handlers never call `SaveChangesAsync`: the inbox pipeline saves and commits (bank-contracts ADR 0012).

## APIs

- Minimal APIs, OpenAPI document per service at `/openapi/v1.json`.
- Routes are plural nouns: `/customers`, `/accounts/{id}/transfers`. The gateway exposes them under `/api/<service>/...`.
- Headers: `X-Customer-Id` (the customer the call acts for, when relevant), `X-Performed-By` (actor id, default `system`). Endpoints that act for a customer reject calls where the resource belongs to another customer (404, not 403).
- Errors use RFC 9457 problem details. Business rule violations return 422 with a `code` field (`insufficient_funds`, `currency_not_active`, ...).
- Every POST that creates or moves money takes an `Idempotency-Key` header; a repeated key returns the original result.

## Reference and seed data

Seeded by each service's EF Core migrations (`HasData`, or SQL inside a migration) so every environment starts the same. Values are demo values and can be changed in the seed files.

**Currencies** (Operations): SEK (2 minor units, active), EUR (2, active), USD (2, active).

**Loan products** (Lending):

| Code | Repayment | Currency | Amount | Term | Annual rate | Penalty per day | Grace | Overdue after | Default after | Payoff min | Payoff cap | Max stages |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CAR | Annuity | SEK | 20,000–600,000 | 12–84 months | 6.95% | 0.05% | 5 days | 1 day | 90 days | 1% | 5,000 | — |
| MORTGAGE | Annuity | SEK | 500,000–10,000,000 | 60–360 months | 4.25% | 0.03% | 5 days | 1 day | 90 days | 1% | 20,000 | — |
| PURCHASE | Staged | SEK | 10,000–300,000 | 3–24 months | 8.50% | 0.05% | 5 days | 1 day | 90 days | 1% | 3,000 | 4 |

**Test cards** (Cards). Numbers start with 9999, which is not issued to real cards:

| Number | Behavior |
| --- | --- |
| 9999 0000 0000 0001 | Approve |
| 9999 0000 0000 0002 | Decline |
| 9999 0000 0000 0003 | InsufficientFunds |

**Employees** (Customers): five employees, `E001`–`E005`, with English names.

**Appointments**: 60-minute slots, 09:00–16:00 Europe/Stockholm, any calendar day after the current business date. An employee has at most one appointment per slot.

**Standing order frequencies**: Weekly, Monthly, Quarterly. A monthly order on the 31st runs on the last day of shorter months.

**Account numbers** (decided by Majid on 2026-10-30, design plan section 2): 9 digits with no meaning. 8 random digits, the first one 1–9 so that no leading zero is lost, plus a Luhn (mod 10) check digit. Generated behind `IAccountNumberGenerator`; Accounts keeps a unique index on the number and draws again on a collision. Stored and sent as a string.

## External services

| Interface | Development and tests | Real |
| --- | --- | --- |
| `IExchangeRateProvider` | Fixed rates (SEK/EUR 11.50, SEK/USD 10.60) | Riksbank SWEA API, latest daily rate; if unreachable, the transfer fails with `fx_unavailable` |
| `IEmailSender` | In-memory in tests, Mailpit when running locally | SES later |
| `IFileStorage` | Temp folder in tests, `./.data/files` locally | S3 in stage 8 |

## Testing

- xUnit v3, Shouldly, Testcontainers. Unit test projects never start containers.
- A stage is done only when `dotnet build` (warnings as errors) and `dotnet test` pass in every service folder the stage touched.
- Hand-calculated expected values in financial tests show the arithmetic in a comment.
- Test projects may use Confluent.Kafka types through the transitive reference of `Bank.Messaging.Kafka` (to read raw topics and test `KafkaHeaders`, which is `internal` with `InternalsVisibleTo`). No `.csproj` other than `Bank.Messaging.Kafka` references Confluent.Kafka directly.

## Accepted limits (demo, not operational)

This system is a learning demo and never runs in production. These gaps were found in the stage 1 review and are deliberately not fixed (Majid, 2026-10-30). Fix them first if the system ever handles real traffic.

- **A failed offset store can stop a service.** In `KafkaConsumerService`, a `KafkaException` from `StoreOffset` (for example after a handler outlived `max.poll.interval.ms` and lost its partition) reaches the outer catch, whose `Seek` then also throws; the hosted service faults and the host stops. That catch also waits `RetryBackoff.DefaultDelays[^1]` instead of the configured delays.
- **Untested failure paths in the Kafka transport.** No test proves that a failed dead-letter send loses nothing (ADR 0013, point 6), and no Kafka-level test sends a message with a missing header or invalid JSON to the DLT. Only the unknown-MODEL case is tested end to end.
- **The Compose healthcheck can pass before the init script finishes.** `pg_isready` without `-h` uses the Unix socket, which the temporary init server already listens on, so a service could start before its role exists. The AppHost is not affected.
- **The atomicity test covers only a failing handler.** It does not cover `SaveChangesAsync` itself failing (for example a business-key violation) after the handler published a reply.
- **Tests and platform use different Kafka images.** Tests use `apache/kafka-native`; the AppHost and Compose use `confluentinc/confluent-local`.

## Language

- Everything written to disk is English.
