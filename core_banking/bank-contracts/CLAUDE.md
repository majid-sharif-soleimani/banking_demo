# bank-contracts

Shared packages for every service: contracts (`Bank.Contracts`), and from slice 1.2 on `Bank.Common`, `Bank.Messaging`, `Bank.Messaging.Kafka`, `Bank.Testing`. Not a service: owns no tables and publishes no events.

## Contracts

- The C# records are the source of truth (ADR 0003). Each event is a `sealed record` implementing `IIntegrationEvent`, with `[Description]` on the type and `public const string SchemaUri = "iglu:se.bank/<event>/jsonschema/<version>"`, its contract version.
- Field descriptions and constraints are attributes on the record: `[property: Description]`, `[property: RegularExpression]`, `[property: MinLength]`, `[property: Range]`. The common fields take theirs from `IIntegrationEvent`.
- `tests/.../Support/ContractSchemaGenerator.cs` generates each schema with `JsonSchemaExporter`. The snapshots in `schemas/se.bank/<event>/jsonschema/<version>.json` are committed and embedded in `Bank.Contracts`.
- `WRITE_SCHEMA_SNAPSHOTS=1 dotnet test` writes missing snapshots, then run `dotnet test` again. To add or change an event use `/new-event`.
- Versions: only ADDITION (`1-0-0` → `1-0-1`, new optional fields) or MODEL (`2-0-0`, anything else). REVISION is not used. Never edit or regenerate a published snapshot; bump `SchemaUri` instead.
- JSON for event payloads always uses `ContractJson.Options` (camelCase, strict numbers, required and non-nullable fields enforced on read). `Money` serializes as `{ "amount": "1234.50", "currency": "SEK" }`.

## Gotchas

- The `schemas` resource names use the build machine's path separator; always read them through `ContractSchemas`, never by raw resource name.
- A contract property of type `decimal`, `double` or `float` fails generation on purpose: use `Money`.
- A validation attribute or CLR type the generator does not map fails generation; add the mapping in `ContractSchemaGenerator`. Nested records are not supported.
- A field with a `pattern` needs a matching sample in `tests/.../Support/SampleFactory.cs`.

## Commands

```bash
dotnet build && dotnet test
dotnet pack -c Release -o ../.local-feed     # then bump the version in each consumer
```
