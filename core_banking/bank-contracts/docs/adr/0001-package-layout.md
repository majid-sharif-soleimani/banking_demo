# 0001. Ship shared code as five small packages from one folder

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-contracts
- Design plan reference: section 4, ADR rows 05, 10, 17; `docs/implementation-decisions.md` "Shared code" and "Repository and packages"

## Context

Six services need the same contracts (`Money`, event records, schemas) and the same messaging plumbing (outbox, inbox, Kafka). Copying that code into each service would let it drift; one big shared library would drag Kafka, EF Core and test helpers into code that needs only a record. Services must also stay independently buildable and deployable (ADR 17), so they cannot reference shared code by project path.

## Decision

The `bank-contracts` folder builds five NuGet packages, all versioned together and starting at `0.1.0`:

| Package | Depends on | Holds |
| --- | --- | --- |
| `Bank.Contracts` | nothing | `Money`, `IIntegrationEvent`, event records with `SchemaUri`, schemas as embedded resources, `SchemaKey`, `ContractJson` |
| `Bank.Common` | `Bank.Contracts` | `IBusinessClock`, `MoneyRounding`, external-service interfaces, `PerformedBy`, `Result<T>` |
| `Bank.Messaging` | `Bank.Contracts`, `Bank.Common`, EF Core | `IEventPublisher`, `IEventHandler<T>`, outbox, inbox, dispatcher, consumer pipeline |
| `Bank.Messaging.Kafka` | `Bank.Messaging`, Confluent.Kafka | The Kafka transport; the only project that references Confluent.Kafka |
| `Bank.Testing` | all of the above, Testcontainers | Test fixtures and fakes; referenced only from test projects |

Packages go to the local feed `core_banking/.local-feed/` with `dotnet pack -c Release -o ../.local-feed`. Consumers reference an exact version and bump it on purpose. Versions follow SemVer: a new event or field is a minor bump; a breaking change to a public type is a major bump.

## Rationale

- Dependencies point one way (contracts at the bottom, Kafka and tests at the top), so a service that only reads events never pulls in Kafka, and swapping the broker touches only `Bank.Messaging.Kafka` (ADR 05).
- Packages instead of project references keep each service folder buildable on its own, as it would be in its own repository.
- One folder and one version for all five keeps releases simple for one developer; the packages change together most of the time.

## Alternatives considered

- One package with everything: rejected; every consumer would depend on Kafka and EF Core, and the "only one project references Confluent.Kafka" rule could not be checked.
- One repository per package with independent versions: rejected; too much release ceremony for one developer, and contracts and messaging change together.
- Project references across service folders: rejected; services would no longer build or deploy independently.

## Consequences

- Changing a contract is a three-step loop: change, pack, bump the version in each consumer. Slower than a project reference, but it makes every contract change visible in the consumer's diff.
- The local feed is not shared between machines; CI creates it empty. Publishing to GitHub Packages comes later and needs Majid.
- The dependency direction must be watched: `Bank.Contracts` must never reference anything else.
