# bank-operations

Owns the bank's currencies and business date, and runs the end-of-day cycle (design plan ADRs 14 and 15). Schema `operations`, role `operations`, topic `bank.operations.events`. Plan: `docs/stages/stage-2-plan.md`.

## Tables

- `currency`: code (PK), minor_units, is_active. SEK, EUR, USD seeded **inactive** (ADR 0002); a currency becomes active only through `POST /currencies/{code}/activate` (idempotent; xmin separates concurrent calls).
- `business_day`: date (PK), status (`Open`, `Closing`, `Closed`, stored as text), opened_at, closed_at. Empty after the migration; `OpenFirstBusinessDay` opens `Operations:StartDate` at startup and publishes `BusinessDayOpened`. The current day is the one not Closed.
- `business_day_completion`: (date, service) as primary key, so a service counts once per date.
- `outbox_message`, `inbox_message` from `modelBuilder.AddBankMessaging()`.

## Events

- Publishes: `CurrencyActivated`, `DayClosed`, `BusinessDayOpened`.
- Consumes: `DayCompleted` from the end-of-day participants (`DayCompletedHandler`; a completion for any day other than the current Closing day throws and ends in the DLT, a repeated (date, service) is a no-op). Event key of day events: the date `yyyy-MM-dd`.

## Configuration

- `Operations:EndOfDayParticipants`: services that must send `DayCompleted` before the next day opens (`["accounts", "lending"]`). Validated at startup: at least one, lower case, no duplicates. `Kafka:SubscribeTo` is derived from it in `Program.cs`; do not set it.
- `Operations:StartDate`: first business date when `business_day` is empty (default `2026-01-01`).
- `ConnectionStrings:operations-db` and `ConnectionStrings:kafka` come from the AppHost. No secret lives in this folder.

## Gotchas

- Running the whole system with the AppHost before stage 4: the default participants (`accounts`, `lending`) do not exist yet, so a close leaves the day Closing. Expected until those services reply.
- `business_day` has a deferred exclusion constraint `ex_business_day_one_current`: at most one day is not Closed, checked at commit.
- `DayCompletedHandler` locks the current day first (`CurrentForUpdateAsync`, `FOR UPDATE`, ADR 0001). Anything else that changes the current day from a handler must take the same lock, or the lost-close race comes back.
- Configuration arrays merge **by index** across sources: `Operations__EndOfDayParticipants__0=x` over `["accounts", "lending"]` gives `["x", "lending"]`. Override the whole list (every index) or edit `appsettings.json`. The test host removes the JSON sources for this reason.

- `Logic/EndOfDay.IsComplete` throws on an empty participant list: "all of none" would close every day without end-of-day work.
- References the shared packages and `Bank.ServiceDefaults` from `../.local-feed` at exact versions. After changing `bank-contracts` or `bank-platform/src/Bank.ServiceDefaults`, pack them and bump the version here. CI packs both from the same commit before restoring.
- The AppHost (`bank-platform`) has a project reference to `Bank.Operations.Api`; a breaking change here also breaks the platform build.

## Commands

```bash
dotnet build && dotnet test                         # integration tests need Docker
dotnet tool restore && dotnet ef migrations add <Name> --project src/Bank.Operations.Api --output-dir Data/Migrations
```

## Tests

- `tests/.../Support/OperationsApp` runs the real `Program.cs` (WebApplicationFactory) on a fresh schema with `InMemoryTransport` instead of Kafka. Settings go through `UseSetting`, because `Program.cs` reads the connection string while it builds.
- `DeliverAsync` hands an incoming event to the inbox pipeline; `Published<T>` reads what the outbox dispatcher sent; `OutboxDrainedAsync` before asserting "nothing more".
