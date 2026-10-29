# bank-contracts

Shared packages for every service: contracts (`Bank.Contracts`), and from slice 1.2 on `Bank.Common`, `Bank.Messaging`, `Bank.Messaging.Kafka`, `Bank.Testing`. Not a service: owns no tables and publishes no events.

## Contracts

- JSON Schemas in `schemas/se.bank/<event>/jsonschema/<MODEL>-<REVISION>-<ADDITION>.json` are the source of truth; they are embedded in `Bank.Contracts` under the same path.
- Each event is a `sealed record` implementing `IIntegrationEvent` with `public const string SchemaUri = "iglu:se.bank/<event>/jsonschema/<version>"`, pointing at the latest version.
- Records and schemas are written by hand and kept in sync by `tests/Bank.Contracts.Tests/ContractTests.cs` (ADR 0002). To add or change an event use `/new-event`.
- JSON for event payloads always uses `ContractJson.Options` (camelCase, strict numbers). `Money` serializes as `{ "amount": "1234.50", "currency": "SEK" }`.
- Never change a published schema file. Add a new version file and bump the version in the record.

## Gotchas

- The `schemas` resource names use the build machine's path separator; always read them through `ContractSchemas`, never by raw resource name.
- A contract property of type `decimal`, `double` or `float` fails the contract tests on purpose: use `Money`.
- A schema field with a `pattern` needs a matching sample in `tests/.../Support/SampleFactory.cs`.

## Commands

```bash
dotnet build && dotnet test
dotnet pack -c Release -o ../.local-feed     # then bump the version in each consumer
```
