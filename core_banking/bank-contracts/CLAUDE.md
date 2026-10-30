# bank-contracts

Shared packages for every service: contracts (`Bank.Contracts`), and from slice 1.2 on `Bank.Common`, `Bank.Messaging`, `Bank.Messaging.Kafka`, `Bank.Testing`. `tools/Bank.Contracts.SchemaTool` is a developer tool and is never packed. Not a service: owns no tables and publishes no events.

## Contracts

- The C# records are the source of truth (ADR 0003). Each event is a `sealed record` implementing `IIntegrationEvent`, with `[Description]` on the type and `public const string SchemaUri = "iglu:se.bank/<event>/jsonschema/<version>"`, its contract version.
- Field descriptions and constraints are attributes on the record: `[property: Description]`, `[property: RegularExpression]`, `[property: MinLength]`, `[property: Range]`. The common fields take theirs from `IIntegrationEvent`.
- `tools/Bank.Contracts.SchemaTool` generates each schema with `JsonSchemaExporter` (`ContractSchemaGenerator`) and writes the snapshots (ADR 0004). The snapshots in `schemas/se.bank/<event>/jsonschema/<version>.json` are committed and embedded in `Bank.Contracts`.
- The tool writes, the tests only check. `dotnet run --project tools/Bank.Contracts.SchemaTool -- write` writes missing snapshots and never overwrites one; `-- check` only compares. Then `dotnet test`. To add or change an event use `/new-event`.
- Versions: only ADDITION (`1-0-0` → `1-0-1`, new optional fields) or MODEL (`2-0-0`, anything else). REVISION is not used. Never edit or regenerate a published snapshot; bump `SchemaUri` instead.
- JSON for event payloads always uses `ContractJson.Options` (camelCase, strict numbers, required and non-nullable fields enforced on read). `Money` serializes as `{ "amount": "1234.50", "currency": "SEK" }`.

## Gotchas

- The `schemas` resource names use the build machine's path separator; always read them through `ContractSchemas`, never by raw resource name.
- A contract property of type `decimal`, `double` or `float` fails generation on purpose: use `Money`.
- A type with a custom `JsonConverter` (such as `Money`) must describe what its converter writes in `public static JsonObject JsonSchema()`, next to the converter; the generator uses that method for any type that has it, and fails for a converter type without it.
- A validation attribute or CLR type the generator does not map fails generation; add the mapping in `tools/.../ContractSchemaGenerator.cs`. Nested records are not supported.
- A field with a `pattern` needs a matching sample in `tests/.../Support/SampleFactory.cs`.

## Commands

```bash
dotnet build && dotnet test
dotnet run --project tools/Bank.Contracts.SchemaTool -- write   # writes missing schema snapshots
dotnet pack -c Release -o ../.local-feed     # then bump the version in each consumer
```
