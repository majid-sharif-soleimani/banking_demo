# Phase 2, stage 6 plan: Languages, evaluation and hosting

Spec: `docs/stages/phase-2/stage-6.md`. Status: built 2026-11-03; done-when commands green except the evaluation run (needs Majid's key). Decisions: agent ADR 0006 (Claude, delegated). Learning document: `docs/learning/phase-2/stage-6-languages-and-evaluation.md`. Overview of the whole phase: `docs/learning/phase-2/overview.md`.

## Slices

1. UI: `i18n.ts`, `App.tsx` (picker, provider), `LoginForm`, `ChatView`, `interactive`, `History`, `chart`, `api.noticeFor`; CSS for the picker and codes; tests `i18n.test.tsx`, Playwright Persian test.
2. Evaluation: `Features/Agent/AgentTools.cs` (shared with the run), `eval/Bank.Agent.Eval` (`EvalCase`, `Grader`, `FakeBank`, `Runner`, `Program`, `cases.json`); tests `EvalTests`.
3. Home bank: backup, five service images, `up -d --wait --no-deps`; checks from a curl container on the bank network; `home-hosting.md` section 8.

## Done-when results (2026-11-03)

- `agent`: unit 76, MCP 38, integration 48 passed; chat-ui 34 passed, build OK; Playwright 7 passed (AppHost).
- Evaluation: the runner and grader tested with a scripted model; the real run waits for Majid's key.
- Home bank: backup written; a customer gets 403 for the trial balance, currency activation and the information request queue, staff get 200; the business day stayed open.

## Blocking questions

None for the code. Majid's actions: set the Anthropic key and `env/bank-mcp.env` (section 8), run the evaluation set once, review and merge the phase 2 branches.
