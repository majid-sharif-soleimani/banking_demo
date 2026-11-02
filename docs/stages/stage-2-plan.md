# Stage 2 plan: Operations

## Context

Stage 1 is Done (docs/progress.md, closed 2026-10-30). Stage 2 builds the first real service, `bank-operations`. It owns two things every other service depends on: the list of currencies and the bank's business date. It also runs the end-of-day cycle. Operations closes the day and publishes `DayClosed`. The participating services (Accounts and Lending, later) do their end-of-day work and reply with `DayCompleted`. When every configured participant has replied, Operations opens the next calendar day and publishes `BusinessDayOpened` (design plan ADRs 14 and 15: coordination by choreography, not by a saga).

The hard parts are not the endpoints. They are:

- keeping the business day a small state machine (Open → Closing → Closed) that never skips or repeats a step;
- opening the next day **exactly once**, even when two `DayCompleted` messages are handled at the same moment by two consumers;
- giving every other service the same business date through one reusable `BusinessDayConsumer`, so no service ever reads the system clock.

All four events this stage needs (`CurrencyActivated`, `BusinessDayOpened`, `DayClosed`, `DayCompleted`) and `IBusinessClock` already exist in `bank-contracts` 0.1.0. No new contract is needed for slices 1–3.

Environment checked: running in `~/repos/banking_demo` on `<machine>` (no worktree).

**Working mode for stage 2 (Majid, 2026-10-30), replacing the stage file's "Majid writes" column for this stage:**
- Claude writes everything: code, tests and all ADRs, including the items the stage file lists under "Majid writes".
- Majid still makes every D1 decision. Claude presents 2–3 options with a recommendation and waits.
- For every slice, Claude writes a learning document, `docs/learning/stage-2/slice-2.N.md`, following `.claude/rules/documentation.md`.
- Slices and checkpoints stay as they are. Claude stops after each slice.

## Decisions made at the start (Majid, 2026-10-30)

- **Seeded currencies are inactive** (blocking question 1, option B). SEK, EUR and USD are seeded with `is_active = false`. A currency becomes active only through `POST /currencies/{code}/activate`, so every service learns about it from `CurrencyActivated`. The import tool activates them first (stage 7). Updated in `implementation-decisions.md` and `stage-7.md`; ADR bank-operations 0002 is written in slice 2.2. Rejected: announcing active currencies at startup (A), and seeding the copy in every consumer (C).
- **Slice 3 is split** into 2.3a (the simple path) and 2.3b (exactly once). Slice 3 combines two heavy topics: the choreography with its state machine, and concurrency. Splitting them means the concurrency decision is made while the simple path already works, so the race can be shown, not only described.
- **Activating an active currency** returns 200 with no new event. An unknown code returns 404.
- **A `DayCompleted` for a date that is not the current Closing day is an error**, not a silent no-op. The handler throws. The consumer then retries it like any other failure and moves it to `bank.<participant>.events.dlt`, where it stays visible (`FailureClassification` treats only schema and format errors as permanent).

## Slices

### 2.1 Logic and data model

- **Folders:** `core_banking/bank-operations/` with `Directory.Build.props`, `Directory.Packages.props`, `nuget.config` (`../.local-feed` + nuget.org), `CLAUDE.md`, `docs/adr/`, solution file; `.github/workflows/bank-operations.yml` (restore, build, test; `paths:` limited to the folder; no publish).
- **Projects:**
  - `src/Bank.Operations.Api` (Minimal API, references `Bank.Contracts`, `Bank.Common`, `Bank.Toolkit.Data`, `Bank.Messaging`, `Bank.Messaging.Kafka` 0.1.0 and `Bank.ServiceDefaults`)
  - `tests/Bank.Operations.UnitTests` (Logic only)
  - `tests/Bank.Operations.IntegrationTests` (Testcontainers, `Bank.Testing`)
- **Files and public interfaces:**
  - `Logic/EndOfDay.cs`: `static bool IsComplete(IReadOnlyCollection<string> participants, IReadOnlyCollection<string> completions)` and `static DateOnly NextDate(DateOnly date)` (the exact signature is Majid's, these are the expected shapes)
  - `Data/OperationsDbContext.cs` with `Currency`, `BusinessDay`, `BusinessDayCompletion` entities, `modelBuilder.AddBankMessaging()`, and the first migration (tables `currency`, `business_day`, `business_day_completion` with unique `(date, service)`, `outbox_message`, `inbox_message`; seed SEK, EUR, USD as inactive)
  - Every table has `created_at`, `performed_by` and `version` (`xmin`)
  - `OperationsOptions` bound from `Operations:EndOfDayParticipants` and `Operations:StartDate` (default `2026-01-01`), validated at startup
  - `Program.cs` with `AddServiceDefaults`, `AddNpgsqlDbContext`, `AddMigrationsOnStartup`, `AddKafkaTransport`, `AddBankMessaging(o => o.ServiceName = "operations")`
  - AppHost: `AddProject<Projects.Bank_Operations_Api>("operations")` with its database and Kafka
- **Tests first:**
  - Unit: `IsComplete` with all, partial, unknown service ignored, duplicates; `NextDate`, including month and year ends
  - Integration: the migration creates the tables and the seed; the unique `(date, service)` constraint rejects a duplicate
  - Platform: the AppHost test sees the `operations` resource healthy
- **Who writes:** Claude (the stage file assigned `EndOfDay` and its tests to Majid; reassigned, see working mode).
- **D1:** none.
- **Learning document:** `docs/learning/stage-2/slice-2.1.md`

### 2.2 Currency endpoints and events

- **Files:** `Features/Currencies/ListCurrencies.cs` (`GET /currencies`), `Features/Currencies/ActivateCurrency.cs` (`POST /currencies/{code}/activate`, publishes `CurrencyActivated` through the outbox in the same `SaveChangesAsync`). Unknown code returns 404 problem details.
- **Tests first (integration):** list returns the seed, all inactive; activating an inactive currency publishes `CurrencyActivated` once; activating it again returns 200 and publishes nothing more; an unknown code returns 404.
- **Who writes:** Claude, including ADR bank-operations 0002 (seeded currencies are inactive).
- **D1:** decided at the start (option B).
- **Learning document:** `docs/learning/stage-2/slice-2.2.md`

### 2.3a Close-day cycle: the simple path

- **Files:**
  - `Features/BusinessDays/GetCurrentBusinessDay.cs` (`GET /business-days/current`)
  - `Features/BusinessDays/CloseBusinessDay.cs` (`POST /business-days/current/close`: Open → Closing, publish `DayClosed`; 409 problem details if not Open)
  - `Features/BusinessDays/OpenFirstBusinessDay.cs` (startup: empty table → open `StartDate`, publish `BusinessDayOpened`; runs after migrations)
  - `Features/BusinessDays/DayCompletedHandler.cs` (`IEventHandler<DayCompleted>`: record the completion; when `EndOfDay.IsComplete`, mark the day Closed, insert `NextDate` as Open, publish `BusinessDayOpened`)
  - `Kafka:SubscribeTo` comes from the same participant list
  - Integration-test stub participant: a small handler in the test project that consumes `DayClosed` and publishes `DayCompleted` for each configured participant
- **Tests first (integration):** startup opens `StartDate` once; close publishes `DayClosed`; closing twice returns 409; full cycle with the stub participant opens the next day; a `DayCompleted` for a date that is not the Closing day fails and changes nothing.
- **Who writes:** Claude.
- **D1:** none.
- **Learning document:** `docs/learning/stage-2/slice-2.3a.md`

### 2.3b Opening the next day exactly once

- **Problem:** two `DayCompleted` messages (from two participants) handled at the same time each see "all complete" and each try to open the next day.
- **Changes:** whatever Majid decides, in `DayCompletedHandler` and possibly the model (a unique index or a lock).
- **Tests first:** "two `DayCompleted` messages open the next day exactly once"; "duplicate `DayCompleted` has no extra effect".
- **Who writes:** Claude, including ADR bank-operations 0001.
- **D1:** how "open the next day exactly once" is guaranteed: unique constraint, row lock, or optimistic concurrency on `Version`.
- **Learning document:** `docs/learning/stage-2/slice-2.3b.md`

### 2.4 `BusinessDayConsumer` in `bank-contracts`

- **Files (location depends on D1):** a `BusinessDayConsumer` (handlers for `BusinessDayOpened` and `DayClosed` that keep a local `business_day` table), its entity and model extension, an `IBusinessClock` implementation that reads the table, and a registration extension (for example `AddBusinessDay()`).
- **Tests first (Claude):** integration tests in `bank-contracts`: after `BusinessDayOpened(D)`, `IBusinessClock.Today` is D; a duplicate or older event does not move the date back.
- **Packaging:** `bank-contracts` version `0.2.0`, packed to `../.local-feed`; Operations stays on whatever it needs (it owns the date and does not consume its own events).
- **Who writes:** Claude, including ADR bank-contracts 0016.
- **D1:** where `BusinessDayConsumer` lives (`Bank.Common` or `Bank.Messaging`, or a new package) and how services use it.
- **Learning document:** `docs/learning/stage-2/slice-2.4.md`

## ADRs this stage needs

All written by Claude in this stage (working mode); Majid decides each D1 and reviews the ADR.

| ADR | Folder | Decision | Slice |
| --- | --- | --- | --- |
| bank-operations 0001 | `core_banking/bank-operations/docs/adr/` | Opening the next business day exactly once | 2.3b |
| bank-operations 0002 | `core_banking/bank-operations/docs/adr/` | Seeded currencies are inactive; activation only through the API | 2.2 |
| bank-contracts 0016 | `core_banking/bank-contracts/docs/adr/` | Where `BusinessDayConsumer` lives and how services use it | 2.4 |

Still open from stage 1: workspace ADR 0003, account number format (Majid).

## Assumptions (Claude's reading of the stage file, accepted by Majid on 2026-10-30)

- A `DayCompleted` from a service that is not in `EndOfDayParticipants` is recorded but does not count (the unit test "unknown service ignored").
- The integration tests use their own participant list (for example `["stub"]`), so the cycle does not depend on the real service names.

## Blocking questions

None. Question 1 (seeded currencies never reach other services) was answered by Majid on 2026-10-30; see "Decisions made at the start".

## Decisions to review (Majid's delegation of 2026-10-31)

On 2026-10-31 Majid asked Claude to finish stage 2 and then stages 3–8 without asking questions. These D1 decisions were made by Claude and are waiting for Majid's sign-off. Each ADR says "to be reviewed by Majid".

| Decision | ADR | Chosen | Main alternative |
| --- | --- | --- | --- |
| Opening the next day exactly once | bank-operations 0001 | Row lock `FOR UPDATE` on the current day in `DayCompletedHandler`, plus a deferred exclusion constraint (one current day) | Optimistic concurrency with broker retries |
| Where `BusinessDayConsumer` lives | bank-contracts 0016 | `Bank.Messaging.BusinessDays`, packages 0.2.0 | A new `Bank.BusinessDays` package |

Sign-off (2026-11-02): Majid reviewed both decisions and confirmed the chosen options. Both ADRs are now Accepted.

## Close-out (2026-10-31)

- Done-when commands: all green (Operations 17 + 44, platform 29, contracts 185).
- `banking-reviewer`: no correctness findings. REQUIRED: Majid signs off the two D1 decisions above (done 2026-11-02). OPTIONAL items applied: exclusion constraint for one current day, comments on the lock/null case and the non-participant race, AppHost note, "Closed day still counts" in ADR 0016.
- `/retro 2` needs Majid: pending.
- Not merged into `master`. Stage 3 continues on `stage-3-lending-logic`, branched from `stage-2-operations`.

## Done when

```bash
cd core_banking/bank-operations && dotnet build && dotnet test
cd core_banking/bank-platform && dotnet build && dotnet test    # AppHost now includes Operations
cd core_banking/bank-contracts && dotnet build && dotnet test   # BusinessDayConsumer, 0.2.0
```
