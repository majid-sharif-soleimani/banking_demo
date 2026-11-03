# 0006. Staff-only reads refuse customers and require an actor

- Status: Accepted
- Date: 2026-11-03
- Scope: workspace (bank-lending, bank-customers, bank-accounts)
- Design plan reference: ADR 13 (simulated authentication at the gateway)
- Decided by: Claude on Majid's delegation of 2026-11-03.

## Context

The review queue, the customer search and the transaction search show many customers' data at once, so a customer must never call them. Authentication is still simulated. The gateway checks a shared API key and then takes the identity headers as given: `X-Customer-Id` marks a customer's call, and `X-Performed-By` names staff, the agent or the import. The existing staff-only actions in Lending (review, approve, disburse, reports) refuse a call that carries `X-Customer-Id` with 403 `staff_only`. They accept a call without `X-Performed-By` and record the actor as `system`. For a read of personal data, "system" leaves no trace of who looked.

## Decision

A staff-only read:

1. returns 403 `staff_only` when `X-Customer-Id` is present (the existing check);
2. returns 401 `missing_actor` when `X-Performed-By` is missing or blank, so every such read is made by a named actor.

Each service has a small `StaffAccess.Problem(HttpRequest)` check that returns the problem or null. It is called first in the endpoint. Existing staff-only actions keep their current behaviour; this ADR applies to the new reads.

## Rationale

- Rule 1 matches what Lending already does, so a customer gets the same answer everywhere.
- Rule 2 costs one line and gives an actor for every read of personal data. This is the minimum a later audit log or real authentication needs.
- When real authentication arrives in phase 2, only `StaffAccess` changes (for example "has the staff role"); the endpoints do not.

## Alternatives considered

- Only refuse `X-Customer-Id` (the current pattern): the least new code, but a call with no identity headers that reaches the service directly (inside the network, past the gateway) would read as "system".
- A role header (`X-Role: staff`): closer to real authorization, but it is one more header taken as given, so it proves nothing more than `X-Performed-By` does, and it adds a concept that phase 2 will replace.
- Real authentication now (JWT, staff identities): explicitly out of scope for this work.

## Consequences

- Staff-only reads and staff-only actions now differ slightly: the reads need an actor, the actions do not. Aligning the actions is a small follow-up.
- Anyone who can reach a service, or who has the gateway key, can still claim to be staff. This is a known limit of simulated authentication until phase 2.
- Each read endpoint gets an integration test that a customer caller is refused and a caller without an actor is refused.
