# 0014. Send each event as one Kafka message: entity id as key, event JSON as value, metadata in headers

- Status: Accepted (already decided in `docs/implementation-decisions.md`; written down in slice 1.3)
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Messaging.Kafka), used by every service
- Design plan reference: section 4, ADR rows 05, 07 and 10; `docs/implementation-decisions.md`, Messaging details

## Context

The outbox hands the transport a `TransportMessage`: message id, schema URI, key, JSON payload, performed-by and the business date (slice 1.2). The transport must put this on Kafka so that:

- a consumer can route and deduplicate a message **before** it parses the JSON (schema URI and message id);
- events about one entity keep their order, which Kafka keeps only inside a partition;
- a consumer in any language can read it without our C# code;
- a message that fails can be copied to a dead-letter topic exactly as it arrived (ADR 0013).

## Decision

| Part of the Kafka message | Content | Example |
| --- | --- | --- |
| Topic | the publisher's topic, `bank.<service>.events` | `bank.operations.events` |
| Key | the id of the entity the event is about, UTF-8 | `bank`, a loan id |
| Value | the event JSON, written with `ContractJson.Options`, UTF-8 | `{"eventId":"…","date":"2026-10-30",…}` |
| Header `message-id` | the event's `EventId`, a GUID in `D` format | `6f1c2b8e-3c55-4d3a-9a11-0d6c1f6e2a10` |
| Header `schema-uri` | the record's `SchemaUri` | `iglu:se.bank/day_closed/jsonschema/1-0-0` |
| Header `performed-by` | who caused the event | `system` |
| Header `occurred-on` | the business date, `yyyy-MM-dd` | `2026-10-30` |

- `KafkaHeaders.ToKafka` writes this; `KafkaHeaders.FromKafka` reads it back and throws `InvalidMessageException` when the key, the value or any header is missing or malformed. Such a message goes straight to the dead-letter topic (ADR 0013).
- Keys and values are handled as **bytes** (`IProducer<byte[], byte[]>`, `IConsumer<byte[], byte[]>`). The text is encoded and decoded in `KafkaHeaders`.
- The producer uses `acks=all` and `EnableIdempotence=true`. `SendAsync` returns only when the broker has stored the message, so the outbox marks a row as sent only when it is.

## Rationale

- **Routing without parsing.** The consumer reads two short headers to decide which record to deserialize into, or whether to skip, deduplicate or dead-letter the message. The payload is parsed once, by the right type.
- **The value is exactly the contract.** The JSON in the value is what the schema snapshot describes (ADR 0003), with no envelope around it. Any reader can validate it against the schema named in the header.
- **The key gives the order.** Kafka sends equal keys to the same partition, so one entity's events arrive in the order the outbox sent them.
- **Bytes keep the evidence.** A dead-lettered message is the exact bytes that arrived, even when they were not valid UTF-8 or JSON.
- **acks=all with idempotence** removes duplicates caused by the producer's own retries. Duplicates from the outbox (ADR 0006) still happen and are handled by the inbox (ADR 0007).

## Alternatives considered

- **An envelope in the value** (`{ "messageId": …, "schemaUri": …, "data": { … } }`, as in CloudEvents' structured mode): rejected. The consumer must parse the JSON before it can route it, and the value no longer matches the schema snapshot.
- **Confluent Schema Registry with its wire format** (a magic byte and a schema id before the payload): rejected in design plan ADR 10. It needs a registry server and ties every reader to it.
- **String serializers** for key and value: simpler code, but a dead-lettered message would no longer be byte for byte what arrived.

## Consequences

- Easier: any tool, such as Kafka UI, shows the routing data as plain headers and the event as plain JSON.
- Harder: the four headers are part of the contract between services. Renaming one is a breaking change for every consumer.
- `occurred-on` and `performed-by` repeat fields of the JSON. They exist for routing, logging and the inbox's audit columns; the JSON stays the source of truth.
