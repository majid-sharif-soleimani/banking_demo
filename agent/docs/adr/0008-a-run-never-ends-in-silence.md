# 0008. A run never ends in silence

- Status: Accepted
- Date: 2026-11-04
- Scope: agent (Bank.Agent.Host, chat-ui)
- Design plan reference: `phase2-design-plan.md` section 6; agent ADR 0007 (cost limits); bank-customers ADR 0002
- Decided by: Claude; Majid delegated the phase 2 decisions on 2026-11-03.

## Context

Twice the admin asked for a customer list and got no answer and no error. The logs of that chat show 118 `customers_search` calls, about 582,000 input tokens and no `show_table` result. The model was guessing two-letter names because the bank refused a search without a filter. The most likely end of each run: it tried to write all the rows into one `show_table` call, reached the limit of 2000 output tokens per model call, and the cut-off call was dropped. The host saw a stream that ended normally. The cut-off itself was not logged at the time, so this cause was inferred from the token counts. A later run confirmed it: the new check logged `finish reason length` after a long run.

## Decision

1. After the agent loop, the host checks two things: did the person get text or a component since the last tool result, and did the last model call end on the output limit (`ChatFinishReason.Length`). If nothing came, or the limit was hit, the run ends with `RUN_ERROR` code `no_answer` and a logged warning with the finish reason. The chat stays open, and the UI shows a notice in the UI language.
2. The admin instructions tell the model to show a long list page by page, one `show_table` per page of 20, and never to search by guessing. The output limit stays at 2000 tokens (Majid's cost profile, ADR 0007).

## Rationale

- The check needs no knowledge of how a model fails: any silent end is caught, including the loop's own iteration limit.
- A page of 20 rows fits well inside one model call, so the limit stays a cost limit and not a limit on the data.

## Alternatives considered

- Raise the output limit: 100 rows would pass, 500 would fail again, and it changes a cost decision.
- A host tool that fetches the list and renders it without the model: one table, fast, cheap, and the rows never reach the model. A new kind of tool, and the model could not discuss the rows. The next step if page-by-page tables turn out too slow or too costly.

## Consequences

- A long list costs one model call per page, and every call also pays for the pages before it. The token log line shows the real cost.
- A cut-off answer can leave partial text in the chat, followed by the notice.
- The limit on one run's cost came with ADR 0009, which also replaced page-by-page lists with a report.
