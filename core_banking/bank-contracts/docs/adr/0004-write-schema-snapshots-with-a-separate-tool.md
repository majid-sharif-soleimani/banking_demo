# 0004. Write schema snapshots with a separate tool, not from a test

- Status: Accepted
- Date: 2026-10-30
- Scope: bank-contracts
- Amends: 0003 (point 4 and the first consequence; everything else in 0003 still holds)
- Design plan reference: section 4, ADR row 10 (unchanged: schemas are still generated from the records and committed as snapshots)

## Context

ADR 0003 generated the schema snapshots inside the contract tests: `WRITE_SCHEMA_SNAPSHOTS=1 dotnet test` wrote a missing snapshot and then failed on purpose, so a second `dotnet test` was needed to embed and check it. A test that writes into the source tree has a side effect, and an environment variable that changes what a test does makes the test suite harder to trust. Majid decided that tests should only check, and that writing a snapshot should be an explicit command of its own.

## Decision

1. A console project, `tools/Bank.Contracts.SchemaTool`, owns schema generation and snapshot files: `ContractCatalog`, `ContractSchemaGenerator` and `SchemaSnapshots` move there from the test project. It is not packed and no service references it.
2. It has two commands, run from `core_banking/bank-contracts`:
   - `dotnet run --project tools/Bank.Contracts.SchemaTool -- write` writes the snapshot of every record whose `SchemaUri` has no file yet.
   - `dotnet run --project tools/Bank.Contracts.SchemaTool -- check` writes nothing and only compares.
   Both exit with code 1 when an existing snapshot differs from its record, and never overwrite it. `check` also exits with 1 when a snapshot is missing.
3. The test project references the tool and uses the same generator, but only reads. `WRITE_SCHEMA_SNAPSHOTS` is removed. A missing or different snapshot fails the test with the `write` command in its message.
4. Tests for the tool itself (`SchemaSnapshotsTests`) run it against a temporary folder: missing files are written, nothing is overwritten, and written files equal the committed ones byte for byte.
5. CI is unchanged: `dotnet test` already fails on a missing or different snapshot.

## Rationale

- One job per part: the tool writes, the tests check. No test has a side effect, and no environment variable changes what a test does.
- The workflow is one command and one test run. The new file is embedded by the build that `dotnet test` starts, so the deliberate failing run is gone.
- Writing a snapshot is a visible, named step, so a new contract version is created on purpose and shows up in the diff.
- The generator stays in one place; the tool and the tests cannot drift apart.

## Alternatives considered

- Keep ADR 0003 (write from the test with `WRITE_SCHEMA_SNAPSHOTS=1`): rejected; a test with a side effect, and a second run that fails on purpose.
- Run the tool from an MSBuild post-build target: rejected; the build would write into the source tree on every build and on CI, and the snapshots are embedded into the same assembly the tool needs, so the file would land after embedding and need a second build.
- Generate on every build and check in CI with `git diff --exit-code schemas/`: rejected; the check only happens on CI, and an incompatible change silently rewrites the published file locally.

## Consequences

- Adding an event: write the record, run the `write` command, review the new file in the diff, then `dotnet test`. Changing one: bump `SchemaUri` first; `write` and the tests say which bump is needed. `/new-event` follows these steps.
- One more project in `Bank.Contracts.slnx`. It has no package of its own and is built with the solution, so `dotnet build` and CI cover it.
- The test project references an executable project. That works in .NET 10; if it ever causes trouble, the generator moves to a small class library that both reference.
- `check` repeats what the snapshot test does, without running the whole test suite. It is not in CI because `dotnet test` already covers it.
