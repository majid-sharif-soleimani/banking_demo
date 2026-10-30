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
