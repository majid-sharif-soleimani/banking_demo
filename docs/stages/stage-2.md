# Stage 2: Operations

Goal: the Operations service owns currencies and the business date, and runs the end-of-day cycle with `DayClosed` / `DayCompleted` / `BusinessDayOpened`.

Read first: `docs/implementation-decisions.md` (time and business date), design plan section 4 ADRs 14–15.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Choreography versus orchestration, state machines (business day status), idempotent handlers under concurrent delivery, configuration instead of hard-coding | Where `BusinessDayConsumer` lives (`Bank.Common` or `Bank.Messaging`) and how other services use it; how "open the next day exactly once" is guaranteed when two `DayCompleted` messages arrive at the same time (unique constraint, row lock, or optimistic concurrency) | `EndOfDay.IsComplete` and `EndOfDay.NextDate` with their tests; the integration test "two DayCompleted messages open the next day exactly once"; ADRs for both decisions |

Slices: (1) Logic and data model, (2) currency endpoints and events, (3) close-day cycle with the stub participant, (4) `BusinessDayConsumer` in `bank-contracts`.

## Folder created

`core_banking/bank-operations`, using the service layout in `CLAUDE.md`. Add it to the AppHost in `bank-platform`.

## Data (schema `operations`)

- `currency`: code, minor_units, is_active. Seeded SEK, EUR, USD, all inactive (bank-operations ADR 0002).
- `business_day`: date, status (Open, Closing, Closed), opened_at, closed_at.
- `business_day_completion`: date, service, completed_at. Unique (date, service).
- Configuration: `Operations:EndOfDayParticipants` = `["accounts", "lending"]` (list, not hard-coded).
- Configuration: `Operations:StartDate` = first business date when the table is empty (default `2026-01-01`).

## Endpoints

| Method | Route | Effect |
| --- | --- | --- |
| GET | /currencies | list |
| POST | /currencies/{code}/activate | activate, publish CurrencyActivated |
| GET | /business-days/current | current date and status |
| POST | /business-days/current/close | status Closing, publish DayClosed; 409 if not Open |

## Handlers

- `DayCompleted`: record completion; when all configured participants have completed the Closing day, mark it Closed, open the next calendar day, publish `BusinessDayOpened`.
- On startup with an empty table: open `StartDate` and publish `BusinessDayOpened`.

## Logic (pure, TDD)

- `EndOfDay.IsComplete(participants, completions)`.
- `EndOfDay.NextDate(date)` (calendar day).

## Tests (minimum)

Unit: completion rules (all, partial, unknown service ignored, duplicates). Integration: close publishes DayClosed; two DayCompleted messages open the next day exactly once; duplicate DayCompleted has no extra effect; closing twice returns 409; currency activation publishes once.

Add a stub participant in integration tests that replies `DayCompleted` so the cycle can be tested before Accounts and Lending exist.

## Also in this stage

A reusable `BusinessDayConsumer` in `Bank.Common` or `Bank.Messaging` (decide, ADR) that every service uses to keep its local `business_day` table and serve `IBusinessClock`. Bump `bank-contracts` to `0.2.0` if it changes.

## Done when

```bash
cd core_banking/bank-operations && dotnet build && dotnet test
cd core_banking/bank-platform && dotnet build && dotnet test    # AppHost now includes Operations
```
