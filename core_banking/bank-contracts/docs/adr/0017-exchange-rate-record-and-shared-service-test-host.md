# 0017. Return an ExchangeRate record, and share the service test host

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-contracts
- Design plan reference: section 2 (exchange rates: "rate, source and timestamp are stored on the transaction"), ADR row 20
- Decided by: Claude on Majid's delegation of 2026-10-31 (public interfaces of shared packages are D1).

## Context

Two changes to shared packages came up at the start of stage 4.

1. **Exchange rates.** The design plan says a converted transfer stores the rate, its source and its timestamp. `IExchangeRateProvider.GetRateAsync` returned only a `decimal`, so Accounts could not know where a rate came from or which day the Riksbank published it for. The interface also gave no way to tell "no rate available", which must become `fx_unavailable` (`implementation-decisions.md`).
2. **The test host.** Operations has a `WebApplicationFactory` host (`OperationsApp`) with an in-memory broker, and Customers and Accounts need the same 150 lines. That is the second and third real use (root `CLAUDE.md`: add an abstraction when a second real use appears).

## Decision

- `Bank.Common` gets `ExchangeRate(From, To, Rate, Source, ObservedOn)`. `IExchangeRateProvider.GetRateAsync` returns it and throws `ExchangeRateUnavailableException` when no rate can be had. The fake in `Bank.Testing` returns source `"test"`.
- `Bank.Testing` gets `BankServiceApp<TProgram>`, the generalized `OperationsApp`:
  - the real service on its own schema;
  - Kafka hosted services removed and `InMemoryTransport` registered;
  - `appsettings*.json` removed, because arrays merge by index;
  - helpers: `DeliverAsync`, `OpenBusinessDayAsync`, `Published<T>`, `OutboxDrainedAsync`, `ScalarAsync`, `Client(performedBy, customerId, idempotencyKey)`, `PostJsonAsync`, `GetJsonAsync`.
- `SequentialAccountNumberGenerator` now returns valid 9-digit Luhn numbers, because `AccountOpened` enforces the format.
- Packages move to `0.3.0`, together with the stage 4 events.

## Rationale

- A record keeps rate, source and date together from the provider to the transfer row. It cannot be filled in from configuration by mistake.
- A typed exception is the one signal the transfer feature turns into 422 `fx_unavailable`.
- One shared test host keeps three services' tests identical in how they fake Kafka and configuration. The appsettings array trap from stage 2 is fixed once.

## Alternatives considered

- **Keep `decimal` and read the source name from configuration:** no interface change, but the observation date would be lost, and the "source" would describe the configuration, not the rate. Rejected.
- **Copy `OperationsApp` into each service:** no package change, but three copies that drift. Rejected. Operations keeps its own `OperationsApp` until it next changes, to avoid churn in a stage under review.

## Consequences

- A breaking change to a shared interface: every implementer must return `ExchangeRate`. Today those are the fake and the Accounts providers.
- `Bank.Testing` now depends on `Microsoft.AspNetCore.Mvc.Testing`, which is fine for a test-only package.
