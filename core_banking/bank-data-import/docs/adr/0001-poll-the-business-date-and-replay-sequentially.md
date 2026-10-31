# 0001. Poll the business date between days, and replay one request at a time

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-data-import
- Design plan reference: section 7 (import through the API, day by day); stage 7 file (D1 decisions)
- Decided by: Claude on Majid's delegation of 2026-10-31. Two D1s for Majid in the stage 7 file.

## Context

The import replays one to two years of history through the gateway, one business day at a time. After each day it closes the day, and must not start the next day's commands until every participant (Accounts, Lending) has finished its end of day and Operations has opened the next date. Otherwise commands would be booked on the wrong date. The stage file also asks how fast and how parallel the replay should run.

## Decision

- **Wait by polling:** after `POST /api/operations/business-days/current/close`, poll `GET /api/operations/business-days/current` every 200 ms until it shows a later date with status Open. A timeout (default 2 minutes per day) fails the import with the date and status it saw.
- **Run sequentially:** one day after another, and within a day one request after another in source order. Calls that depend on events another service must receive first (an account for a just-registered customer, a loan for a just-opened account) are retried on their 422 code for up to a minute, with the same Idempotency-Key. A code that is really final (such as `repayment_account_invalid` for a wrong account) therefore costs that minute before the row is skipped.

## Rationale

- Polling uses only the public API through the gateway, the same path an operator or the agent would use. The import stays a plain HTTP client, with no Kafka client and no consumer group.
- Sequential replay keeps the order of the source data, makes every run identical, and makes a failure easy to locate. At about 100 customers, the time is dominated by the end-of-day cycle, not by the requests, so parallelism would save little.

## Alternatives considered

- **Wait for `BusinessDayOpened` on Kafka:** an exact signal, but the import would need Kafka access, a consumer group and the contracts package's transport. That is a second integration path just for the tool. Rejected.
- **Customers in parallel within a day:** faster for large imports, but the order of a customer's own transactions must still be kept, the 409 retries get more frequent, and runs stop being reproducible. Rejected for now.

## Consequences

- Each day costs at least one end-of-day round trip through Kafka (about 0.5 s in the test AppHost). 600 days take minutes, which is acceptable for a one-off load.
- The fixture test showed that the import must call the services' own resource paths under the gateway prefix (`/api/accounts/accounts/...`), and must identify itself on reads too.
