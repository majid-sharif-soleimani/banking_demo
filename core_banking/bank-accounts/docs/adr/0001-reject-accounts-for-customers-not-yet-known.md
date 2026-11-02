# 0001. Reject an account for a customer that Accounts does not know yet

- Status: Accepted (reviewed and confirmed by Majid on 2026-11-02)
- Date: 2026-10-31
- Scope: bank-accounts
- Design plan reference: section 4, ADR rows 04 and 11; section 6 (CustomerRegistered → Accounts)
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 4 file.

## Context

Customers registers a person and publishes `CustomerRegistered`. Accounts learns about the customer only when that event reaches its `customer_ref` table, usually within a fraction of a second, but not instantly. A caller who registers a customer and immediately opens an account can arrive at Accounts first. Accounts may not ask Customers (no service-to-service calls, design plan ADR 04).

## Decision

`POST /accounts` for a customer missing from `customer_ref` answers **422 `customer_not_found`**, with a detail saying the customer reaches Accounts within moments and the call may be retried. Nothing is stored. The same applies when `X-Customer-Id` names another customer.

## Rationale

It is the simplest rule that keeps Accounts' data correct: an account always belongs to a customer Accounts knows. The gap is short, and the import tool and the agent (phase 2) can retry with a short backoff. There is no extra state, no background job and no half-open account.

## Alternatives considered

- **Accept and verify later:** store the account as "pending" and confirm it when `CustomerRegistered` arrives, or reject it after a timeout. The caller gets an immediate success, but money could be deposited into an account whose owner never appears, and a pending state plus a timeout job adds complexity. Rejected.
- **Wait briefly:** poll `customer_ref` for a few seconds inside the request. It hides the gap from the caller, but holds a request thread and a database connection, and fails anyway if the event is slow. Rejected.

## Consequences

- Callers (the import tool in stage 7, the agent in phase 2) must retry `customer_not_found` a few times after registering a customer.
- The test `The_same_request_succeeds_once_CustomerRegistered_has_arrived` documents the behaviour.
