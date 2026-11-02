# 0002. Seed currencies inactive and activate them only through the API

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-operations
- Design plan reference: section 5 (Currency: Code, MinorUnits, IsActive), section 6 (CurrencyActivated → All), ADR row 07
- Decided by: Majid (D1, at the start of stage 2, option B). Written by Claude (stage 2 working mode).

## Context

Operations owns the currencies. Every other service keeps a local copy of the active ones (`currency_ref` in Accounts, Cards and Lending), filled only from `CurrencyActivated`, because services never read another service's schema (design plan ADR 04). `implementation-decisions.md` first said that SEK, EUR and USD are seeded **active** by the Operations migration. A seeded row is never activated, so `CurrencyActivated` would never be published for it. Every local copy would then stay empty, and Accounts (stage 4) would reject every new account with "currency not active". With all three already active and no endpoint to create a currency, `POST /currencies/{code}/activate` could also never publish anything.

## Decision

- The first Operations migration seeds SEK, EUR and USD with 2 minor units and `is_active = false`.
- `POST /currencies/{code}/activate` is the only way a currency becomes active. In one `SaveChangesAsync` it sets `is_active`, records `performed_by` and adds `CurrencyActivated(Code, MinorUnits)` to the outbox, keyed by the code. `OccurredOn` is Operations' current business date.
- Activating an active currency returns 200 with the currency and publishes nothing. Two concurrent activations are separated by the row's `xmin`: the loser gets `DbUpdateConcurrencyException`, which rolls back its outbox row, and it returns 200 as well. An unknown code returns 404 problem details with `code = currency_not_found`. The code is not case sensitive.
- Whoever prepares an environment activates the currencies first: the import tool (stage 7, step 0), the integration tests of each service, or a person calling the API.

## Rationale

Every active currency then reaches every service through the same path as any later activation: the outbox, Kafka and each consumer's idempotent handler. There is one way in, so there is one thing to test, and no service ever needs a special case for "seeded" data. Making the endpoint a no-op for active currencies keeps it safe to call on every run of the import tool.

## Alternatives considered

- **Announce active currencies at startup.** Operations would publish `CurrencyActivated` for every active currency not yet announced (an `announced_at` column), like opening the first business day. This keeps a fresh environment usable without a setup step, but adds a startup job and a second way for the event to appear. Rejected by Majid in favour of a single path.
- **Seed the same currencies in every consumer's migration.** No event is needed, but the reference data is defined in several places and drifts, and it breaks "one owner per piece of data". Rejected.

## Consequences

- A fresh environment has **no usable currency** until someone activates one. A service started alone accepts no money operation until then. This is visible and easy to fix, but surprising the first time.
- Tests of later services must activate (or deliver `CurrencyActivated`) before they open accounts or loans.
- There is no deactivation. If one is ever needed, it is a new event (`CurrencyDeactivated`) and a business decision first.
- `implementation-decisions.md` ("Reference and seed data"), `docs/stages/stage-2.md` and `docs/stages/stage-7.md` were updated on 2026-10-30.
