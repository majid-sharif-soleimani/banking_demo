---
paths:
  - "core_banking/**/Features/**/*.cs"
  - "core_banking/bank-contracts/**/*"
---

# Messaging and contracts rules

- Event contracts are JSON Schemas under `bank-contracts/schemas/se.bank/<event_name>/jsonschema/<MODEL-REVISION-ADDITION>.json`, with a matching C# record in `Bank.Contracts`. The schema is the source of truth; a contract test keeps the record in sync.
- SchemaVer: ADDITION for new optional fields, REVISION for changes that may break some consumers, MODEL for breaking changes. Never edit a published schema version; add a new file.
- Every event carries: `EventId`, `OccurredOn` (business date), `PerformedBy`, and Money as `{ amount, currency }`.
- Handlers: check the inbox first, apply the effect, write any resulting events to the outbox, commit once. A handler that is replayed must change nothing.
- Topic per publishing service (`bank.<service>.events`), message key = the entity id the event is about.
- After three failed attempts a message goes to `<topic>.dlt`. Do not swallow exceptions to avoid this.
- Nothing outside `Bank.Messaging.Kafka` may reference Confluent.Kafka.
