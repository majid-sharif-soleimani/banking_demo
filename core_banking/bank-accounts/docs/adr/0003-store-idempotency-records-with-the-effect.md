# 0003. Store idempotency records in the same transaction as the effect

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-accounts
- Design plan reference: `implementation-decisions.md`, APIs ("Every POST that creates or moves money takes an Idempotency-Key header; a repeated key returns the original result")
- Decided by: Claude on Majid's delegation of 2026-10-31.

## Context

A client that times out does not know whether its deposit happened, so it sends the request again. Without protection, money moves twice. The workspace decided that money POSTs take an `Idempotency-Key`. How the key is stored was open.

## Decision

- Table `idempotency_record`, keyed by (operation, key). For example operation `deposit:{accountId}`, so one key can be reused for another account or action. It holds the SHA-256 of the request body, the status code and the response body as **text**.
- `IdempotentCommand.RunAsync` wraps every money command:
  1. the key is required (400 `idempotency_key_required`);
  2. an existing record with the same body hash replays its stored response byte for byte, and one with a different body is 422 `idempotency_key_reused`;
  3. otherwise the command runs, and its success is saved **in the same `SaveChangesAsync`** as the balance change, the entry and the outbox row;
  4. two requests with one key at the same moment both try to insert the record, the primary key lets one win, and the other replays its answer.
- Business failures (422) are not stored, so a retry after the situation changed (for example after a deposit) is evaluated again.

## Rationale

Saving the record with the effect means there is no window where money moved but the key is unknown, or the key is stored but money did not move. The database's primary key resolves the concurrent case without locks.

## Alternatives considered

- **A cache (memory or Redis) checked before the command:** fast, but outside the transaction. A crash between the effect and the cache write loses the key and the next retry moves money again. Rejected.
- **Store failures too (Stripe's model):** a retry always sees the first answer. But an "insufficient funds" answer would stick even after the customer deposits money. Rejected for this demo.
- **jsonb for the response:** it reorders keys, so a replay would not be identical. `text` is used.

## Consequences

- Idempotency records are never deleted in phase 1. A retention job can be added later.
- The same command logic serves endpoints and, without a key, internal callers such as standing orders (slice 4.5), which use their own business keys.
