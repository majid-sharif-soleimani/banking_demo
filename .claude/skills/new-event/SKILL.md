---
name: new-event
description: Add or change an integration event contract in bank-contracts. Use when a service needs to publish a new event or change an existing one.
argument-hint: [EventName]
---

Add or change the integration event `$ARGUMENTS`.

1. Check section 6 of `phase1-design-plan.md` for the event's publisher, consumers and effect. If the event is not there, ask Majid before adding it.
2. In `core_banking/bank-contracts`:
   - New event: create `schemas/se.bank/<snake_case_name>/jsonschema/1-0-0.json`.
   - Changed event: add a new version file; choose MODEL, REVISION or ADDITION per the messaging rules. Never edit an existing version file.
3. Add or update the C# record in `src/Bank.Contracts`, with the schema URI as a constant on the record.
4. Run the contract tests (record ↔ schema, and compatibility with the previous version). Fix until green.
5. Bump the package version (SemVer: major for a MODEL change, otherwise minor).
6. List the consuming services that must update the package.
