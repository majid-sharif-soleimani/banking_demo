# 0003. Generate event schemas from the C# records and keep them as snapshots

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-contracts
- Supersedes: 0002
- Design plan reference: section 4, ADR row 10 (changed by this decision); section 8, contract tests

## Context

ADR 0002 made the JSON Schema files the source of truth and kept hand-written C# records in sync with them through a reflection test. Every event was written twice, and the reflection comparer, its own tests and the `/new-event` steps all existed only to keep those two copies equal. For one developer, one monorepo and about 25 events, all of them in .NET, that is more machinery than the risk calls for. The real risk is a publisher changing an event in a way its consumers cannot read, and the check for that can be kept without writing the schema by hand.

## Decision

1. The C# records in `Bank.Contracts` are the source of truth. Descriptions and constraints are attributes on the record: `[Description]` on the type and on properties, and `[RegularExpression]`, `[MinLength]` and `[Range]` on properties. The common fields take theirs from `IIntegrationEvent`.
2. Each record's `public const string SchemaUri` is its contract version (Iglu-style URI, for example `iglu:se.bank/day_closed/jsonschema/1-0-0`). On the wire it travels in the `schema-uri` header, so the payload has no separate version field.
3. The schema is generated with .NET's `JsonSchemaExporter` and `ContractJson.Options` (`tests/Bank.Contracts.Tests/Support/ContractSchemaGenerator.cs`). `Money` gets its string-amount shape; a bare `decimal`, `double` or `float` and an unmapped validation attribute are rejected.
4. The generated schema is committed as a snapshot at `schemas/se.bank/<event>/jsonschema/<version>.json` and embedded in the package, as before. `WRITE_SCHEMA_SNAPSHOTS=1 dotnet test` writes a missing snapshot and never overwrites an existing one.
5. Contract tests in CI fail when:
   - a record no longer matches the snapshot of its current `SchemaUri`, or a snapshot is missing;
   - two consecutive versions of a schema are not a legal step;
   - a serialized sample does not validate against its snapshot.
6. Evolution rule: a version may only add optional fields (ADDITION, `1-0-0` → `1-0-1`). Any other change is a new MODEL (`2-0-0`). REVISION is not used and stays 0. A published snapshot is never edited.
7. `ContractJson.Options` reads strictly (`RespectNullableAnnotations`, `RespectRequiredConstructorParameters`), so a consumer rejects the same messages the schema rejects.
8. REST APIs need nothing extra: each service's OpenAPI document is generated from its request and response DTOs (`docs/implementation-decisions.md`, APIs).

## Rationale

- One copy of each contract, in the language every publisher and consumer uses. The compiler checks every use of a field.
- The snapshot keeps what ADR 0002 valued: a language-neutral contract that is reviewed in the diff, and a red build for an incompatible change.
- The generator and snapshot writer are about 200 lines of test code that uses an in-box .NET API. There is no build step, no extra package in `Bank.Contracts`, and no schema registry, so the cost stays at zero.

## Alternatives considered

- Keep ADR 0002 (hand-written schemas and a reflection test): rejected; every event is written twice and the comparer is code that has to be maintained only for that.
- Generate C# records from the schemas (NJsonSchema at build time): rejected for the reasons in ADR 0002. Generated types are hard to shape (`Money`, `DateOnly`, constants) and the build gets a code generation step.
- Records only, no snapshots: rejected; an incompatible change would show up only at runtime in another service, and non-.NET consumers (the phase 2 agent, reports) would have no contract to read.

## Consequences

- Adding an event: write the record with its attributes and `SchemaUri`, run `WRITE_SCHEMA_SNAPSHOTS=1 dotnet test`, then `dotnet test`. Changing one: bump `SchemaUri` first; the test message says which bump is needed. `/new-event` walks through both.
- A refactoring in C# that would change a published contract fails the snapshot test, so it cannot happen silently.
- The schema can express only what the generator maps. A new CLR type or validation attribute in a contract fails generation with a message until a mapping is added.
- `JsonSchemaExporter` skips its transform callback for some repeated types, so the generator applies `Money` and the attributes in its own pass over the record's properties. Nested records are not supported yet; contracts hold flat fields and `Money`.
- The five snapshots generated on this date are equal, as JSON, to the hand-written schemas they replace. The published contracts did not change.
- Consumers handle an unknown ADDITION with their latest known record and send an unknown MODEL to the dead-letter topic (slice 1.3).
