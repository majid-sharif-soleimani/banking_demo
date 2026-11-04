# 0011. Loan numbers without the schedule, and fuller usage logs

- Status: Accepted
- Date: 2026-11-04
- Scope: agent (Bank.Agent.Mcp, Bank.Agent.Host)
- Design plan reference: `phase2-design-plan.md` sections 3.3 and 6; agent ADR 0009 (changes its limit and its cache setup)
- Decided by: Majid chose the five goals on 2026-11-04. Claude chose how to build them, on his delegation of 2026-11-03.

## Context

The first real runs on Haiku cost little, but the numbers showed four problems. A real loan's schedule is about 24 KB, roughly 10,000 tokens, and `loans_list` sent it for any loan question, after which every later call of the run paid for it again. The limit of 150,000 input tokens could stop a normal run of about eight calls, for about one cent. The log held one sum per run, so it could not show what each call read from the cache. And in a run of a long chat, the first call appeared to read nothing from the cache.

That last one has a cause in our request. The only cache point moved with the last block, and `ChatHistory` sends the last 20 messages. Once a chat passes 20 messages the window slides, so the messages at the start of the request change on every run. A cache read needs the same prefix up to a cache point, and the point nearest to the start of the request was past the first change. The tools and instructions, which never changed, were written to the cache again.

## Decision

1. **`loan_summary` for customers and `customer_loan_summary` for the admin.** They read the bank's loans and answer with numbers only: counts, totals, the oldest open installment, the next one not yet due, and optionally one chosen installment. `loans_by_classification` answers the same way, since 100 schedules could reach about a million tokens. `loans_list` and `customer_loans` stay for showing a whole schedule, and their descriptions and the instructions say so.
2. **The MCP server adds the numbers up.** The sums are plain decimal additions of what the bank answered, in `LoanFigures`. The overdue amount, the classification and the days past due are passed on from the bank. Nothing about a payment or a payoff is predicted.
3. **A cache point on the instructions.** `CostControlledModel` moves the instructions into the request's system block with its own `cache_control`. The moving cache point stays as well.
4. **`MaxInputTokensPerRun` is 600,000.** Four times the old limit, as Majid asked. Tokens from the cache still count.
5. **Fuller logs.** One line per model call (input, read from the cache, written to it, output, time, finish reason). One line per tool (outcome including `refused`, result size, time). One line per run (model, calls, tools, tool result size, history size, time). Counts only, never content.
6. **Seq stays the lasting place for logs.** Its data is in a folder on the host machine that survives a rebuild of the agent, while `docker logs` is lost with the container.

## Alternatives considered

- **A summary endpoint in Lending.** The bank would own the sums, but phase 1 is closed except for bug fixes, and the MCP server can add them without a new contract.
- **Cutting the schedule down inside `loans_list`.** It would break showing the whole schedule, which a person may really want.
- **A history window that moves in steps.** It would also keep the history in the cache, but the history is small next to the tools and instructions. The measured numbers can show later whether it is worth it.
- **A price table in the host for a cost per run.** Prices change and would go stale. Seq can compute cost from the token counts.
- **A second log file on a volume.** Seq already keeps the logs, and a file would need rotation and its own reader.

## Consequences

- A model that needs a detail of an installment asks with `installmentNumber` instead of reading the schedule.
- `loans_by_classification` no longer shows schedules; an admin who wants one uses `customer_loans`.
- The new limit lets a runaway run spend more: up to 600,000 input tokens, about 6 cents at Haiku's plain input price and much more on another model.
- The instructions name the person, so the cache of the tools and instructions is per person.
- None of this has run on the real model yet. The request is tested over a fake HTTP handler, and the cache savings remain to be read from the new log lines.
