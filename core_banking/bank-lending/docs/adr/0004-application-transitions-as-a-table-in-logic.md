# 0004. Model application transitions as one table in Logic/

- Status: Accepted (to be reviewed by Majid)
- Date: 2026-10-31
- Scope: bank-lending
- Design plan reference: section 5 (application statuses); `docs/stages/stage-5.md` (allowed transitions)
- Decided by: Claude on Majid's delegation of 2026-10-31. A D1 for Majid in the stage 5 file.

## Context

A loan application moves through eight statuses, driven by API calls (review, request information, approve, reject, cancel, disburse) and by events from Customers (`InformationReceived`, `AppointmentScheduled`). An illegal step, such as approving without review or disbursing twice, must be impossible from every path. The statuses and their rules are a business specification that should be readable in one place and tested exhaustively.

## Decision

- `Logic/ApplicationStateMachine` holds the allowed transitions as a dictionary `(status, action) → new status`. `Apply` returns `Result<ApplicationStatus>`, and every other pair is `invalid_transition`. It is pure, with no I/O.
- `Features/Applications/Transitions.ApplyAsync` is the only code that changes a status. It asks the state machine, appends a history row and publishes `LoanApplicationStatusChanged`, all in the caller's unit of work.
- An endpoint answers 409 `invalid_transition` when the step is illegal.
- An event handler (`InformationReceived`, `AppointmentScheduled`) logs and ignores an illegal step. The fact is true (an appointment was booked), the application has just moved on (for example it was cancelled), and dead-lettering would only create noise.
- Review actions are refused with 403 `staff_only` when the call carries `X-Customer-Id`. A customer may cancel their own application.

## Rationale

- The whole specification is 15 lines of data, so a test can check all 72 status/action pairs: 15 allowed and 57 rejected.
- One writer for statuses means the history and the event can never be skipped.
- No library is needed for a flat table without guards or actions.

## Alternatives considered

- **Stateless library:** a richer API (guards, entry actions, diagrams), but a new dependency and a configuration DSL for something a dictionary expresses directly. Rejected.
- **Checks in each endpoint:** no central table, but the rules are scattered, cannot be tested as a whole, and are easy to get wrong in the event handlers. Rejected.

## Consequences

- A new status or action means one line in the table and an updated test row.
- Events that arrive after an application left the expected status are ignored with a warning. Monitoring the log shows them.
