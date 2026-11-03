# bank-data-import

Console tool `Bank.DataImport`: loads ~100 customers with up to two years of history into a **fresh** bank through the gateway (design plan section 7). Not a service. Plan: `docs/stages/stage-7-plan.md`.

## Running

```bash
# put the CSV export in data/raw/ (gitignored; the tool never downloads it), start the AppHost, take the gateway URL from the dashboard
dotnet run --project src/Bank.DataImport -- --gateway http://localhost:<port> [--force]
```

- Without the export: `python3 tools/generate-sample-data.py` writes synthetic CSVs with the default column names into `data/raw/` (100 customers, one year, fixed seed; `--years 2`, `--seed N`). The history meets the selection targets and the product ranges.

- Fresh bank only: it stops before writing anything when a customer already exists (409 on `IMP-<id>`), and stops before closing any day when no account was opened. `--force` lets existing customers be skipped instead (they cannot be looked up, so a second run never resumes). The replay is anchored on the current business date. Writes `data/import-report.md` (gitignored), also a partial one when it stops (exit 4). Exit code 0 only when verification passed.
- **Column names** of the real export are mapped in `appsettings.json` (`Import:Columns`); the defaults match the test fixture and must be checked against the real files.

## How it works

- `Logic/ImportLogic.cs` (pure, unit tested): selection (≥30 loans, ≥10 late, ≥15 standing orders, fixed seed), date shift (last 2 years → from StartDate), amounts × factor (default 10.0) to SEK, loan type → product by whole word, account type and frequency mapped only when known (otherwise skipped and reported), idempotency keys from source rows, equal PURCHASE stages.
- `Importer.cs`: activates SEK/EUR/USD, registers customers, opens accounts (retrying `customer_not_found`), reads each product's `MaxStages`, then per day: loans (submit, review, approve, disburse), transactions, standing orders, loan payments as deposits; closes the day and polls the business date (ADR 0001). Then verifies balances against statements and the trial balance.
- Paths go through the gateway as `/api/<service>/<service path>`, for example `/api/accounts/accounts/{id}/deposits`. Every request sends `X-Performed-By: import`, plus `X-Api-Key` when `Import__ApiKey` is set (the gateway asks for it outside Development).

## Tests

- Unit: `tests/Bank.DataImport.UnitTests` (no Docker).
- Integration: `ImportThroughAppHostTests` starts the whole AppHost (Docker, about 40 seconds), imports `Fixture/*.csv` (5 synthetic customers), and checks that a second run is refused.
