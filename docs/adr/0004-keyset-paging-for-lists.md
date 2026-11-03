# 0004. Page every list with an opaque keyset cursor

- Status: Accepted
- Date: 2026-11-03
- Scope: workspace (bank-lending, bank-customers, bank-accounts)
- Design plan reference: none (phase 1 extension for the phase 2 agent)
- Decided by: Majid chose keyset paging on 2026-11-03; on his delegation the same day, Claude decided the response shape (no total count).

## Context

The phase 2 agent needs three lists: the loan application review queue, a customer search and a transaction search across accounts. The transaction table only grows, and the review queue gets new rows while someone is reading it. No endpoint pages today, so there is no pattern to follow. Offset paging gets slower on deep pages, and when rows arrive between two calls it shows rows twice or skips them. For the agent, a skipped transaction in an investigation is a wrong answer, not a cosmetic glitch.

## Decision

All three lists use the same contract:

- Request: `limit` (default 50, between 1 and 200) and an optional `cursor`.
- Response: `{ "items": [...], "nextCursor": "..." | null }`. `nextCursor` is null on the last page. There is no total count.
- Each list has a fixed, total sort order that ends with the row id, so no two rows tie: applications `(created_at, id)` oldest first, customers `(name, id)`, transactions `(business_date, sequence)`.
- The cursor is base64url-encoded JSON holding the sort values of the last row returned. Clients treat it as opaque. The next page is read with a row comparison in SQL, for example `WHERE (created_at, id) > (@c, @id) ORDER BY created_at, id LIMIT @limit + 1`. The extra row only tells whether there is a next page.
- A cursor that cannot be decoded returns 400 `invalid_cursor`. A `limit` out of range returns 400 `invalid_limit`.
- Each service has its own small `Paging` helper (encode, decode, limit check). It is not in a shared package.

## Rationale

- Keyset reads stay fast at any depth when an index matches the sort order, and they stay stable while rows are added. The agent never sees a row twice or misses one.
- With no total count, there is no extra `COUNT(*)` on every call. Counting a transaction search with open filters scans everything it matches. The agent only needs to know whether there is more.
- The cursor is opaque, so a service can change its sort columns without breaking clients.
- A per-service helper of about 30 lines avoids a release of every shared package (`Bank.Common` 0.6.1 → 0.7.0 and a bump in each service). The contract lives in this ADR, so the copies agree.

## Alternatives considered

- Offset paging (`page`, `pageSize`): simplest, and allows jumping to page N. Rejected for the instability and the cost on deep pages described above.
- Keyset for transactions and the queue, offset for customers (about 100 rows): each list gets the method that suits it, but the agent would have to learn two contracts.
- Always return `totalCount`: rejected for the cost of a count on every page. A `includeTotal=true` flag can be added later without breaking the contract.
- One shared `Paging` class in `Bank.Common`: rejected for now (see Rationale); move it there if a fourth service needs it.

## Consequences

- Clients cannot jump to page N or show "page 3 of 12". The agent and a future staff UI page forward only.
- Every list needs an index that matches its filter plus its sort order; each slice says which index it adds.
- A cursor carries sort values (a date, a name). The customer cursor holds a customer's name, so it must not appear in a URL: the customer search takes it in a POST body (ADR 0005).
- Three copies of `Paging` must be kept the same; a change to the contract changes all three.
