# Learning Log

What Majid learned in each stage, in his own words. Written with `/retro` at the end of every stage. Each stage's concepts and decisions are listed in the "Learning" section of its `docs/stages/stage-N.md`.

<!-- /retro appends one section per stage below this line -->

## Stage 1: Foundation: contracts, messaging, platform (2026-10-30)
Concepts: value objects, contract-first design, JSON Schema, SchemaVer, contract tests; the dual-write problem, transactional outbox, at-least-once delivery, idempotent consumers, inbox; Kafka topics, partitions and keys, ordering, consumer groups, offset commits, poison messages, dead-letter topics; infrastructure as code, least-privilege database roles, service defaults, OpenTelemetry basics.
Decisions I made: schemas generated from records (bank-contracts 0003), snapshots written by a separate tool (0004), outbox polling with SKIP LOCKED (0006), inbox key (MessageId, HandlerName) (0007), Bank.Toolkit packages (0011), Dapper and then back to EF Core (workspace 0001, 0002; bank-contracts 0012, superseding 0008-0010), retry in the consumer then dead-letter, business-key violations retried (0013), subscribed services in configuration (0015); review findings not fixed for the demo (implementation-decisions, Accepted limits); account number format (ADR to write).
What I understood: not recorded; Majid skipped the retro questions.
Still unclear: not recorded.
What I would change: not recorded.
Working-style changes for next stage: not recorded.

## Stage 2: Operations (2026-11-02)
Concepts: choreography versus orchestration, the business day state machine (Open, Closing, Closed), idempotent handlers under concurrent delivery (row lock `FOR UPDATE`, READ COMMITTED, exclusion constraint), the local `business_day` table with "latest date wins" and one date per scope, configuration instead of hard-coding.
Decisions I made: made by Claude on my delegation of 2026-10-31, reviewed and confirmed on 2026-11-02: lock the current business day while handling DayCompleted (bank-operations 0001); the business-day consumer lives in Bank.Messaging (bank-contracts 0016).
What I understood: in choreography each service waits for messages and acts on them. It needs no coordinator and probably suits a system with many services better, but it makes it harder to trace one operation and its effects in the other systems. It has no single point of failure and, implemented correctly, can be more resilient. In orchestration a coordinator sends the messages and moves a state machine. In a system as small as ours I think orchestration would be the better fit.
Still unclear: nothing; all stage 2 concepts are clear.
What I would change: nothing; keep the close-day flow as choreography.
Working-style changes for next stage: in multiple-choice questions, put each option on its own single line. In a Persian conversation, every sentence, paragraph, bullet point and list item starts with a Persian word.

## Stage 3: Lending pure logic (TDD) (2026-11-02)
Concepts: TDD (red, green, refactor), pure functions ("functional core, imperative shell"), property-based tests and invariants, result types instead of exceptions.
Decisions I made: immutable schedule records (bank-lending 0001), `Result<T>` with error codes (0002); in the rule interpretations (0003) I confirmed points 6 and 9, changed point 5 (an overpayment prepays later items, split pro rata between principal and interest for a partly covered item, no interest waived), and chose that the early payoff accrual subtracts interest already prepaid.
What I understood: not recorded; Majid skipped the retro questions.
Still unclear: not recorded.
What I would change: not recorded.
Working-style changes for next stage: not recorded.

