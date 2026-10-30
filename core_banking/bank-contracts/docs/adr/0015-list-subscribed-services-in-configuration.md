# 0015. List the services a consumer reads from in configuration

- Status: Accepted (D1, decided by Majid in slice 1.3)
- Date: 2026-10-30
- Scope: bank-contracts (Bank.Messaging.Kafka), used by every service
- Design plan reference: section 4, ADR row 05; `docs/implementation-decisions.md`, Messaging details

## Context

Each service publishes all its events to one topic, `bank.<service>.events` (ADR 0014). A consumer registers handlers by event type, for example `AddEventHandler<DayClosed, RunEndOfDay>()`. But nothing in the event says who publishes it: the schema URI is `iglu:se.bank/day_closed/jsonschema/1-0-0`, with no service in it. So the Kafka consumer cannot work out by itself that Lending must read `bank.operations.events`. Something has to name the topics, and that is part of the public surface of a shared package.

## Decision

- Each service lists the **services** it reads from in `Kafka:SubscribeTo`, for example:

  ```json
  { "Kafka": { "SubscribeTo": [ "operations" ] } }
  ```

- `AddKafkaTransport` turns each name into its topic with `KafkaTopics.Events(name)`, and the consumer subscribes to those topics in group `bank.<service>`.
- Names are validated at startup: lower-case letters and digits, starting with a letter. A topic name such as `bank.operations.events` fails with a message that says to use the service name.
- An empty list means the service only publishes; no consumer is started.
- `KafkaTopicInitializer` creates the subscribed topics and their `.dlt` topics if they do not exist yet, so the order in which services start does not matter.
- Messages of events the service has no handler for are skipped (`ProcessOutcome.NotSubscribed`) and their offset is stored.

## Rationale

- **Small and explicit.** One line of configuration per service, readable in its `appsettings.json`. It needs no new concept in `Bank.Contracts`.
- **Few edges.** Each service reads from one or two others (design plan section 6), so the list stays short.
- **The risk is caught by tests.** A missing entry means a handler never receives anything; each service's integration tests for that handler fail.

## Alternatives considered

- **Subscribe by pattern** (`^bank\..+\.events$`): no configuration, but every service reads all bank traffic, its own topic included, and throws most of it away. The boundaries between services are no longer visible in Kafka UI.
- **Name the publisher in the contract** (`[PublishedBy("operations")]` on each record) and derive the topics from the registered handlers: the topics can never drift from the code, and the owner of each event is documented in the contract. Rejected for now: it adds a concept to `Bank.Contracts`, and `TestEvent`, published by two test services, would need a workaround. It can be added later without changing services' handlers.

## Consequences

- Easier: what a service consumes is visible in one place, next to its other settings.
- Harder: adding a handler for an event from a new publisher needs two changes, the handler and the configuration.
- To watch: if a missed `SubscribeTo` entry ever reaches a deployed system, move to the publisher-in-contract option in a new ADR.
