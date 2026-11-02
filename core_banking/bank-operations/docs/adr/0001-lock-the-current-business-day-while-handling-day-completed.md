# 0001. Lock the current business day while handling DayCompleted

- Status: Accepted (reviewed and confirmed by Majid on 2026-11-02)
- Date: 2026-10-31
- Scope: bank-operations
- Design plan reference: section 4, ADR rows 08 and 15
- Decided by: Claude on Majid's delegation of 2026-10-31 ("do every slice without asking me"). This was a D1 for Majid in the stage file; he should review it.

## Context

Operations opens the next business day when the last end-of-day participant sends `DayCompleted`. Accounts and Lending finish at about the same time, so their two messages can be handled at the same moment, by two consumer threads or two instances. Each handler runs in its own transaction under PostgreSQL's default READ COMMITTED isolation. It inserts its completion and then asks "have all participants completed?". Neither transaction sees the other's uncommitted row. Two outcomes are possible:

- **Lost close:** both see only their own completion, both stop, and the day stays Closing forever. The integration test showed this on its first run.
- **Double open:** if the timing lets both see both rows, both try to close the day, insert the next one and publish `BusinessDayOpened`.

The inbox does not help, because these are two different messages.

## Decision

`DayCompletedHandler` first locks the current business day with `SELECT …, xmin FROM business_day WHERE status <> 'Closed' ORDER BY date DESC LIMIT 1 FOR UPDATE` (`BusinessDayQueries.CurrentForUpdateAsync`). The lock is held by the transaction the inbox pipeline opened for the handler, until the pipeline commits. Only then does the handler check for a duplicate, check the date, insert the completion and evaluate `EndOfDay.IsComplete`. A second handler waits on the lock. When it gets the row, it sees the first handler's committed completion, so exactly one of them sees "complete" and opens the next day. The primary key on `business_day.date` stays as a second safety net against a double open.

## Rationale

The lock turns a race into a queue at the one place where the decision is made: the current day's row. It is correct on the first attempt, needs no retry, and puts no extra load on the Kafka consumer. Only one row is locked, for a few milliseconds, once per participant per day, so contention is not a concern.

## Alternatives considered

- **Unique constraint only** (on the next date, which already exists as the primary key). It prevents a double open: the second insert fails, the message is retried, and the retry finds a duplicate. It does **not** prevent the lost close, which the test showed is the likely failure. Rejected as the only protection; kept as a safety net.
- **Optimistic concurrency on `xmin`.** Every handler would also update the day row, for example by touching `performed_by`, so two concurrent handlers conflict and one gets `DbUpdateConcurrencyException`. The consumer then retries it (200 ms, 1 s, 5 s), and the retry sees both completions. It is correct, but it works through exceptions and broker retries. A conflict blocks the partition for at least 200 ms, and four conflicts in a row send a valid message to the dead-letter topic. Rejected: the lock is simpler to reason about for a decision that happens once per day.
- **SERIALIZABLE isolation for the handler.** PostgreSQL would detect the anomaly and abort one transaction, which is then retried. It has the same retry cost as optimistic concurrency, and it would mean a pipeline option for one handler. Rejected.

## Consequences

- The handler needs an open transaction. The inbox pipeline always provides one (bank-contracts ADR 0012); calling the handler outside the pipeline would lock nothing.
- `FOR UPDATE` needs raw SQL (`FromSql`), and the query must name `xmin`, a gotcha already known from the outbox dispatcher.
- Under READ COMMITTED, a waiting `FOR UPDATE` re-checks its `WHERE`. If the first handler closed the day, the waiting one gets no row. It then either finds its completion as a duplicate (no-op) or throws `UnexpectedDayCompletedException`, which is the rule for a completion of another day.
- A non-participant's `DayCompleted` racing the last participant's is either recorded (it took the lock first) or rejected as "another day" (it waited and the day closed). Both follow the rule; which one happens depends on timing.
- A deferred exclusion constraint (`ex_business_day_one_current`, review of 2026-10-31) makes the database refuse a second current day, independent of the lock.
- `ExactlyOnceTests` runs the race 15 times in one test. Without the lock it fails on the first race; with it, it has passed every run.
