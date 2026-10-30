---
paths:
  - "core_banking/**/Features/**/*.cs"
  - "core_banking/bank-contracts/**/*"
---

# Messaging and contracts rules

- Event contracts are C# records in `Bank.Contracts` (the source of truth). Their JSON Schemas are generated and committed as snapshots under `bank-contracts/schemas/se.bank/<event_name>/jsonschema/<MODEL-REVISION-ADDITION>.json`; contract tests fail when a record drifts from its snapshot.
- Versions: ADDITION for new optional fields, MODEL for any other change. REVISION is not used. Never edit a published snapshot; bump `SchemaUri` and write a new one.
- Every event carries: `EventId`, `OccurredOn` (business date), `PerformedBy`, and Money as `{ amount, currency }`.
- Handlers: check the inbox first, apply the effect, write any resulting events to the outbox, commit once. A handler that is replayed must change nothing.
- Topic per publishing service (`bank.<service>.events`), message key = the entity id the event is about.
- A failed message is retried three times (four executions in total), then goes to `<topic>.dlt`; permanent failures go there at once (bank-contracts ADR 0013). Do not swallow exceptions to avoid this.
- Nothing outside `Bank.Messaging.Kafka` may reference Confluent.Kafka.
