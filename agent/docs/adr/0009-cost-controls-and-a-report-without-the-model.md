# 0009. Cost controls for a run, and a customers report that does not pass through the model

- Status: Accepted; the limit and the cache setup changed in agent ADR 0011
- Date: 2026-11-04
- Scope: agent (Bank.Agent.Host, Bank.Agent.Mcp, eval, chat-ui)
- Design plan reference: `phase2-design-plan.md` sections 6 and 7; agent ADRs 0007 and 0008
- Decided by: Majid (2026-11-04): the report without the model instead of background work, a limit of 150,000 input tokens per run, prompt caching, and prompt compression. Claude for how each is built and for the other limit values.

## Context

Asking for all customers with their accounts and loans made one run use 1,144,438 input tokens, about 2.38 USD, and it still ended without an answer. In about an hour the chat used about 4.5 USD and reached the account's limit.

The cause is how an agent loop pays. Each model call sends everything before it again, tool results included, at full price. Reading 100 customers one by one means about 200 tool results, sent again on every later call. Writing a table also means the model writes every row into a tool call, which hits the output limit. Nothing stopped a run that kept going.

Majid first suggested a background worker. That helps with waiting, but it has the same model, limits and cost, so it was not chosen for this problem.

## Decision

1. **A report without the model.** The tool `show_customers_report` takes only what the report should hold (contact details, accounts, loans). The host fetches the rows through the host-only MCP tool `host_customers_report`, shows them as a table and tells the model only how many there are.
2. **A limit per run.** `Agent:Model:MaxInputTokensPerRun`, 150,000. Above it the run stops before the next model call and ends with `RUN_ERROR` `run_budget`. Tokens read from the cache count too: the limit is simple, and it also stops a loop that would be cheap per call but never ends.
3. **Prompt caching.** Each call sets the provider's top-level `cache_control`, which moves the cache point to the last block by itself.
4. **Prompt compression, in two places.**
   - Within a run, the provider's context editing (`clear_tool_uses_20250919`): above `ClearToolResultsAbove` (30,000 input tokens) it replaces old tool results with a note, keeps the newest `KeepToolResults` (5), and acts only when it saves `ClearToolResultsAtLeast` (5,000).
   - Between turns, `ChatHistory` cuts a stored component longer than `HistoryComponentLength` (1,000 characters) and says so. Typed text is never cut.
5. **The right error.** The UI shows a note per failure code and no longer calls every failure a bank outage. The log gets the provider's status and message, cut to 300 characters. Any other exception still logs its type only.

Caching and clearing live in one small wrapper in `LanguageModel.cs`, so the rest of the host still does not name the provider (decision A1). They need Anthropic's beta client (`client.Beta.AsIChatClient`). The document reader stays on the plain client: a single call with nothing to reuse.

6. **The agent's model is `claude-haiku-5-5`** (Majid, 2026-11-04, changes the default of ADR 0007). `Agent:Model:Name` still changes it without a rebuild.

## Rationale

- The report removes the cost instead of reducing it: its rows use no model tokens, in that turn or later ones.
- Clearing was chosen over the provider's compaction, which writes a summary. Compaction hands back a block the caller must send again, and this SDK's `IChatClient` does not carry that block through, so its state would be lost silently. Clearing needs no state on our side.
- `clear_at_least` exists because clearing throws away the cache after that point. Clearing a little would cost more than it saves.

## Alternatives considered

- **Background work with a notice when ready**: solves waiting, not cost or limits. It needs the bank token kept alive past the 5-minute idle timeout and has no person there to confirm changes. To be added when a task is really long and cannot be made shorter.
- **A raised output limit**: larger tables would pass, but it costs more per call and only moves the limit.
- **A new bank read of customers with accounts and loans**: services may not call each other (non-negotiable rule), so this would need a read model fed by events. Too much for one report. The MCP server combines the reads instead, 8 at a time, for at most 1,000 customers.

## Consequences

- The model does not see the report's rows, so it cannot answer questions about them without searching. The table in the history is cut for the same reason.
- A loan has no number in the bank, so the report shows the loan id with its product and status.
- Cleared results can make the model read something again. The token log line now shows the cached part, so the effect can be measured.
- A task above the limit stops partway and asks for a smaller part, even if it would have finished.
- The savings are not measured yet: the request fields are tested over a fake HTTP handler, but no real call has been made with them.
