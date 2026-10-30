# 0002. Keep event records and JSON Schemas in sync with a reflection test

- Status: Superseded by 0003 (2026-10-30)
- Date: 2026-10-30
- Scope: bank-contracts
- Design plan reference: section 4, ADR row 10 (JSON Schema is the source of truth, plus C# records)

## Context

Every event exists twice: as a JSON Schema file (the contract, readable by any language) and as a C# record (what our code uses). If they drift, for example a field is added to the record but not the schema, a consumer that trusts the schema misreads or rejects messages, and the problem shows up only at runtime in another service. We need a rule for how the two stay the same, and it must run in CI (ADR row 10: "CI checks version compatibility").

## Decision

Write both by hand. `tests/Bank.Contracts.Tests/ContractTests.cs` checks, for every `IIntegrationEvent` record in `Bank.Contracts`:

1. It declares `public const string SchemaUri`, and that schema exists and is the latest version of its name.
2. Its public properties and the schema's `properties` have the same names (camelCase), the same JSON type and format (`Guid` → string/uuid, `DateOnly` → string/date, `int` → integer, `Money` → object with string `amount` and `currency`), and `required` lists exactly the non-nullable ones. A bare `decimal`, `double` or `float` is rejected.
3. A sample instance serialized with `ContractJson.Options` validates against the schema (JsonSchema.Net, formats enforced). This catches what reflection cannot see, such as patterns and minimums.

Every schema file is also checked: its path, its `self` block and its `SchemaUri` agree; the first version is `1-0-0`; and each version is a legal SchemaVer step from the previous one (ADDITION only adds optional fields, REVISION never removes or retypes a field, MODEL may change anything).

The comparer has its own tests that feed it deliberately wrong schemas, so a bug in the check cannot make every contract test pass.

## Rationale

- The schema stays the real source of truth, as ADR row 10 requires, and the records stay ordinary hand-written C# that can carry `Money`, `DateOnly` and doc comments.
- No build-time code generator, so the build stays plain `dotnet build` and the code you read is the code that runs.
- With about 25 events in phase 1, writing each one twice is a small cost, and the test failure message says exactly what differs.

## Alternatives considered

- Generate the C# records from the schemas (for example NJsonSchema at build time): rejected because generated types are hard to shape (`Money` as a string-amount object, `DateOnly`, records with constants) and add a build step; worth it only with many more events.
- Generate the schemas from the records (`JsonSchemaExporter`): rejected because C# would become the real source of truth, which contradicts ADR row 10, and a refactoring in C# could silently change a published contract.

## Consequences

- Adding an event means writing a schema file and a record, then running the tests; `/new-event` walks through it.
- The check runs after the change, not before: drift is caught at `dotnet test` or in CI, not prevented while typing.
- It checks shape, not meaning. Renaming the meaning of a field while keeping its name and type passes the test; reviews and SchemaVer discipline must catch that.
- New CLR types in contracts (enums, lists) need a mapping in `RecordSchemaComparer` and a sample in `SampleFactory`; the test says so when one is missing.
- Published schema files are never edited; a change is a new version file.
