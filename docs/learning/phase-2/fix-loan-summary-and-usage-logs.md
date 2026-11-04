# Fix: loan numbers without the schedule, and logs that show token use

## What we want and why

A simple loan question was sending about 10,000 tokens of schedule to the model, and the logs could not show where the tokens went. We want the model to get only the numbers it needs, and we want each model call to be measurable.

## The building blocks

- **`LoanFigures`** (MCP server) turns the bank's loans into counts and totals. `loan_summary`, `customer_loan_summary` and `loans_by_classification` use it.
- **`CostControlledModel`** (host) now gives the instructions their own cache point, next to the moving one.
- **`AgUiEndpoint`** (host) logs each model call, each tool and each run.
- **Seq** keeps those logs. Group `agent`, filter `@Resource.log_group = 'agent'`.

## How to read a run in Seq

Search for `@Message like 'Model call%'` and read one chat from its first call:

- `CachedTokens` is cheap, about a tenth of the input price.
- `CacheWrittenTokens` costs more than plain input, about 1.25 times.
- A later call that reads about as much as the call before it wrote means that the cache works.
- A first call with a large `CacheWrittenTokens` and `CachedTokens` near zero is the miss this fix should remove.

## Why it is designed this way

A prompt cache matches from the start of the request up to a cache point. Our history is a sliding window, so its start changes in a long chat, and the only cache point sat after that change. The tools and instructions come before the history and do not change, so a cache point right after them keeps matching. Details and alternatives are in agent ADR 0011.
