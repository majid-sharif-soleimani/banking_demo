# 0005. Send searches that carry personal data as POST with a body

- Status: Accepted
- Date: 2026-11-03
- Scope: workspace (bank-lending, bank-customers, bank-accounts)
- Design plan reference: none (phase 1 extension); `.claude/rules/secrets.md` (personal data)
- Decided by: Claude on Majid's delegation of 2026-11-03.

## Context

The customer search filters by name, email and phone. A URL is copied to many places the bank does not control well: access and proxy logs, traces, browser history, monitoring tools and error reports. Today `Microsoft.AspNetCore` and `Yarp` log at Warning, so request lines are not logged. But a single log-level change, or a new tracing exporter, would start writing every searched email address to disk. A body is not copied by any of these by default. The question is which list endpoints may use GET with query parameters and which must use POST.

## Decision

- An endpoint whose filters (or cursor) can contain personal data is `POST /<resource>/search` with a JSON body. The body holds the filters, `limit` and `cursor`. It answers 200, stores nothing and needs no `Idempotency-Key`. This covers `POST /customers/search` and `POST /transactions/search`. The transaction search has Money ranges with a currency and a customer filter, which is easier to express as JSON.
- A list whose filters are only ids, enum values and dates stays a GET with query parameters: `GET /loan-applications?status=&customerId=&limit=&cursor=`.
- No search writes its filter values to a log. Logs may name the endpoint, the actor and the number of results.

## Rationale

- Personal data stays out of every URL, whatever the logging setup becomes.
- Plain filtered lists keep the HTTP meaning of GET: cacheable, bookmarkable and easy to try in a browser.
- The rule is easy to apply: "can a filter or the cursor identify a person? Then POST."

## Alternatives considered

- GET for everything, with the logging configured to drop query strings: correct HTTP, but it depends on every log, proxy and exporter being configured right, now and later. One mistake leaks the data.
- POST for every list, including the review queue: one style everywhere, but it gives up GET semantics where there is no personal data.
- Search by an opaque, short-lived search id (POST the filters, then GET results by id): keeps GET for paging, but it needs server-side state for a read. Too much for this demo.

## Consequences

- `POST /customers/search` and `POST /transactions/search` look like writes to tools that judge by method. The OpenAPI summary says "stores nothing".
- A search cannot be shared as a link. That is acceptable, since these are staff-only (ADR 0006).
- Each new list must apply the rule. A GET list that later gains a personal-data filter moves to POST.
