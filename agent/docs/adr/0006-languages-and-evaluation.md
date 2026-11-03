# 0006. Translate the UI with one dictionary and evaluate the agent with rule-graded cases over a fixed bank

- Status: Accepted
- Date: 2026-11-03
- Scope: agent (chat-ui, Bank.Agent.Host, eval/Bank.Agent.Eval), bank-platform (home hosting)
- Design plan reference: `phase2-design-plan.md` sections 2.1, 7.3 question 5, 9 step 6
- Decided by: Claude. Majid delegated the phase 2 decisions on 2026-11-03.

## Context

The agent already answers in the language of the message (business question 5), but the UI around it was English only, and Persian needs right-to-left layout. The behaviour of the agent with the real model (does it wait for Confirm, ignore injections, keep the language?) had only been tested with scripted models. And the phase 2 bank changes were not yet on the home bank.

## Decision

1. **One dictionary, no i18n library.** `chat-ui/src/i18n.ts` holds every UI text in en, sv and fa, typed so a missing text fails the build. A React context gives the language; a picker in the header chooses it (saved per browser, defaulting to the browser's language).
2. **Right to left by the page direction.** Persian sets `dir="rtl"` on `<html>`; the CSS uses logical properties, so the layout mirrors by itself. Messages keep `dir="auto"`; codes and the chart's plot stay left to right.
3. **Follow-up messages in the UI language.** "I confirmed the change." is sent in the chosen language, so the agent does not switch to English after a click.
4. **An evaluation set of rule-graded cases.** `agent/eval/Bank.Agent.Eval` runs fixed conversations with the chat's own instructions and tools (`AgentTools.For`, now shared with the run) over a fixed fake bank, and grades by rules: tools called or not, components shown, answer patterns, answer language. No model grades another model.
5. **Deploy only what changed, after a backup.** The home bank got the five changed service images with `up -d --wait --no-deps`; the agent waits for Majid's secrets.

## Rationale

Three languages and about 70 short texts do not need a library; a typed object is easy to read and to check. Logical CSS was already in place, so right to left costs one attribute. Rule-based grading is cheap, deterministic and explains every failure in one line; a fixed bank makes runs comparable, so a lower score means the model or the instructions changed, not the data.

## Alternatives considered

- i18next or react-intl: plurals and formats we do not need yet, one more dependency.
- The UI language from the customer's stored preference: only known after login, and the admin has none.
- Grading with a second model (LLM as judge): better for open answers such as reports, but costs more and needs its own checking. A later addition for the management report.
- Running the evaluation against the AppHost: real data, but slower, and results drift with the data.

## Consequences

- A new UI text needs three translations; the type makes a missing one a build error.
- The evaluation costs money per run and needs the key; it is run by hand, not in CI.
- Rule checks can pass a poor answer or fail a good one phrased differently; failures are read, not only counted.
