# bank-operations

Owns the bank's currencies and business date, and runs the end-of-day cycle (design plan ADRs 14 and 15). Schema `operations`, role `operations`, topic `bank.operations.events`. Plan: `docs/stages/stage-2-plan.md`.

## Tables

- `currency`: code (PK), minor_units, is_active. SEK, EUR, USD seeded **inactive** by the first migration (ADR 0002, slice 2.2); a currency becomes active only through the API.
- `business_day`: date (PK), status (`Open`, `Closing`, `Closed`, stored as text), opened_at, closed_at. Empty after the migration; the service opens `Operations:StartDate` on its first start (slice 2.3a).
- `business_day_completion`: (date, service) as primary key, so a service counts once per date.
- `outbox_message`, `inbox_message` from `modelBuilder.AddBankMessaging()`.

## Events

- Publishes: `CurrencyActivated`, `DayClosed`, `BusinessDayOpened`.
- Consumes: `DayCompleted` from the services in `Kafka:SubscribeTo`.

## Configuration

- `Operations:EndOfDayParticipants`: services that must send `DayCompleted` before the next day opens (`["accounts", "lending"]`). Validated at startup: at least one, lower case, no duplicates. Keep `Kafka:SubscribeTo` in step with it.
- `Operations:StartDate`: first business date when `business_day` is empty (default `2026-01-01`).
- `ConnectionStrings:operations-db` and `ConnectionStrings:kafka` come from the AppHost. No secret lives in this folder.

## Gotchas

- `Logic/EndOfDay.IsComplete` throws on an empty participant list: "all of none" would close every day without end-of-day work.
- References the shared packages and `Bank.ServiceDefaults` from `../.local-feed` at exact versions. After changing `bank-contracts` or `bank-platform/src/Bank.ServiceDefaults`, pack them and bump the version here. CI packs both from the same commit before restoring.
- The AppHost (`bank-platform`) has a project reference to `Bank.Operations.Api`; a breaking change here also breaks the platform build.

## Commands

```bash
dotnet build && dotnet test                         # integration tests need Docker
dotnet tool restore && dotnet ef migrations add <Name> --project src/Bank.Operations.Api --output-dir Data/Migrations
```
