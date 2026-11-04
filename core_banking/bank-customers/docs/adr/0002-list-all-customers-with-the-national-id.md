# 0002. Staff can list every customer, and the list shows the national id

- Status: Accepted
- Date: 2026-11-04
- Scope: bank-customers, agent
- Design plan reference: none (phase 1 extension for the phase 2 agent); changes ADR 0001; workspace ADRs 0004–0007
- Decided by: Majid for the two requirements (2026-11-04). Claude for how a blank filter behaves.

## Context

Using the chat as admin, Majid asked for a list of all customers with their national ids, and the agent could not give it. `POST /customers/search` required at least one filter and left the national id out of its summary (ADR 0001, "a broad search exposes less"). The agent tried to get around this by searching for two-letter combinations: 118 searches and over 500,000 input tokens in two messages, with no list at the end.

## Decision

1. `POST /customers/search` with no filter returns every customer, in the same order and keyset pages as any search. It stays staff only (workspace ADR 0006).
2. `CustomerSummary` includes `nationalId`.
3. A filter that is given but blank (`""`, `"  "`) is a 400. Only leaving a filter out means "no filter".

## Rationale

- Staff can already read each national id through `GET /customers/{id}`, so the list adds convenience, not a new right.
- Point 3 protects other callers. `bank-identity` looks a customer up with `{nationalId, limit: 1}`. If a blank value meant "no filter", a bug that lost the value would return the first customer, and a login could land on someone else's account. `bank-identity` rejects a blank national id itself; this is the second lock.

## Alternatives considered

- A separate `GET /customers` list endpoint: a second query and a second contract for the same rows; the national id in its responses is no different.
- A national id only in the agent's tool, read one by one with `GET /customers/{id}`: one call per customer, which is the kind of loop this change removes.

## Consequences

- A national id is also the test login's username and password (workspace ADR 0007), so anyone with staff access can log in as any customer. The data is synthetic and the logins are public by design; a real login method must replace this before real data comes near the bank.
- Through the chat, the national ids of the listed customers reach the model provider and stay in the stored chat. `customer_get` already did this one customer at a time; the list does it for many.
- The list is linear in the number of customers, like every search (ADR 0001). Paging keeps each answer small.
