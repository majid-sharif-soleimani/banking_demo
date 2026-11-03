# Phase 2, stage 6: Languages, evaluation and hosting

Goal: the chat speaks the bank's three languages (the UI in English, Swedish or Persian, Persian right to left), a repeatable evaluation set measures how the agent behaves with the real model, and the bank's phase 2 changes run on the host machine next to the live bank.

Read first: `phase2-design-plan.md` sections 2.1, 6, 7.3 (question 5), 9 (step 6); agent ADRs 0002 to 0005; `core_banking/bank-platform/deploy/home-hosting.md` section 8.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Internationalisation without a library, right-to-left layout with logical CSS, evaluating a model by rules, fixed test data for comparable runs, deploying only what changed | None asked (delegated, 2026-11-03). Claude's choices: agent ADR 0006. | Nothing |

Slices: (1) the UI in three languages and right to left, (2) the evaluation set, (3) the home bank.

## Chat UI

- `chat-ui/src/i18n.ts`: every UI text in en, sv and fa; a language picker in the header (remembered per browser, first default from the browser). Persian sets `lang="fa"` and `dir="rtl"` on `<html>`.
- The messages the UI sends after an action are in the UI language, so the agent keeps answering in it.
- Codes (`IMP-C001`) stay left to right inside Persian text; the chart's plot stays left to right.

## Evaluation set

- `agent/eval/Bank.Agent.Eval`: 16 cases in `cases.json` (balances in three languages, off-topic, a transfer that must wait, the loan advisor, a direct injection, the admin's trial balance, an injected note and document, no recommendation, approval on the admin's word, closing the day with checks, arrears follow-up, a Persian management report).
- The same instructions and tools as the chat (`AgentTools.For`), over a fixed fake bank; graded by rules (`Grader`): tools called or not, components shown, answer patterns, answer language.
- Needs `Agent:Model:ApiKey`; costs money per run; results in `agent/eval/results/` (gitignored).

## Home bank

- Backed up, then Operations, Ledger, Customers, Accounts and Lending rebuilt and recreated (no uploads in Customers). Staff-only checks verified inside the bank network.
- The agent and `bank-mcp` wait for Majid's two secrets (`home-hosting.md` section 8).

## Tests (minimum)

- Chat UI: placeholders in every language; notices in the chosen language; the saved choice; switching to Persian translates and turns the page right to left.
- Browser: Persian on a phone: right to left, own message on the left, no sideways scroll.
- Unit: the grader (each broken rule is one failure), the language guess, every case's tools exist for its role, the test payslip is a valid PDF, the runner through the real tools with a scripted model.

## Done when

```bash
(cd agent && dotnet build && dotnet test)
(cd agent/chat-ui && npm ci && npm test && npm run build)
(cd agent/tests/chat-ui-e2e && npm ci && npx playwright test)   # with the AppHost running
dotnet run --project agent/eval/Bank.Agent.Eval                 # Majid, with his key
```
