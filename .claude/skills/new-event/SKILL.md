---
name: new-event
description: Add or change an integration event contract in bank-contracts. Use when a service needs to publish a new event or change an existing one.
argument-hint: [EventName]
---

Add or change the integration event `$ARGUMENTS`.

1. Check section 6 of `phase1-design-plan.md` for the event's publisher, consumers and effect. If the event is not there, ask Majid before adding it.
2. In `core_banking/bank-contracts/src/Bank.Contracts`, add or change the record (ADR 0003, 0004):
   - New event: `[Description]` on the type, attributes for field descriptions and constraints, and `public const string SchemaUri = "iglu:se.bank/<snake_case_name>/jsonschema/1-0-0"`.
   - Changed event: bump `SchemaUri`: ADDITION if you only add optional fields (a default value or nullable), otherwise MODEL. Never edit an existing snapshot.
3. From `core_banking/bank-contracts`, run `dotnet run --project tools/Bank.Contracts.SchemaTool -- write` to write the new snapshot, review it in the diff, then run `dotnet test` (snapshot match, and compatibility with the previous version). Fix until green.
4. Bump the package version (SemVer: major for a MODEL change, otherwise minor).
5. List the consuming services that must update the package.
