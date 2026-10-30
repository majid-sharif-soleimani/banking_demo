# 0005. Group contract types into Events and Schema namespaces

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-contracts
- Design plan reference: none (package layout from ADR 0001 is unchanged; only the namespaces inside `Bank.Contracts` change)

## Context

`Bank.Contracts` started with most types in the root namespace, and the event records in one namespace per publishing service (`Bank.Contracts.Operations`, plus `Bank.Contracts.Testing` for `TestEvent`). `IIntegrationEvent` sat in the root next to `Money`, and the schema plumbing (`SchemaKey`, `ContractSchemas`, `IJsonSchemaProvider`) was mixed in with the contract types. Every service references this package, so its namespaces are part of its public surface and are cheapest to change now, before any service uses it.

## Decision

Majid decided:

| Folder and namespace | Holds |
| --- | --- |
| `Bank.Contracts` (root) | `Money`, `CurrencyMismatchException`, `ContractJson` |
| `Events/`, `Bank.Contracts.Events` | `IIntegrationEvent`, every event record of every service, and `TestEvent`, in one flat folder |
| `Schema/`, `Bank.Contracts.Schema` | `SchemaKey`, `ContractSchemas`, `IJsonSchemaProvider` |

The namespace always matches the folder. `ContractJson` stays in the root because it is the wire format every publisher and consumer uses, not schema handling. `Money` stays in the root because it is a contract type that events and other code use; it only implements `IJsonSchemaProvider`. The schema tool and the test project keep their own layout.

## Rationale

- Everything about events is in one place, and everything about schemas in another, so a reader finds a type by what it is.
- A flat `Events` folder is simple: one namespace to import in every service, and no need to decide which service owns a shared event.
- Namespace equals folder, as for every other namespace in the workspace, so the IDE and the reader agree on where a type lives.

## Alternatives considered

- Keep one namespace per publishing service (`Bank.Contracts.Operations`, later `.Accounts`, `.Lending`): rejected by Majid in favour of one place for all events.
- `Events/` as a parent with one subfolder per publisher (`Bank.Contracts.Events.Operations`): keeps the publisher visible in the namespace, at the cost of longer names; not chosen.
- Move only `IIntegrationEvent` and keep the per-service folders: rejected; the interface every service implements would sit in one service's folder.
- New folders but namespaces left at `Bank.Contracts`: rejected; folder and namespace would disagree.
- Move `TestEvent` out of `Bank.Contracts` (to `Bank.Testing` or a test project): rejected for now; the schema tool and `ContractSchemas` only scan this assembly, and the test event must go through the same generation and contract tests as real events. Can be revisited in slice 1.2.

## Consequences

- The publisher of an event is no longer visible in its namespace. It is stated in the record's `[Description]` and in section 6 of the design plan, and the topic (`bank.<service>.events`) is chosen by the publishing service, not from the namespace.
- `Events/` will hold dozens of records by stage 8. If that becomes hard to navigate, the parent-with-subfolders layout above is a mechanical change (namespaces only); the JSON Schemas do not contain namespaces, so the wire format is unaffected.
- `TestEvent` is still shipped in the package next to real events; nothing but convention stops a service from publishing it.
- `/new-event` and the bank-contracts `CLAUDE.md` point new records to `Events/`.
