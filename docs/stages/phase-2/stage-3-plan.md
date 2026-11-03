# Phase 2, stage 3 plan: Forms, diagrams and changes

Spec: `docs/stages/phase-2/stage-3.md`. Status: built 2026-11-03; done-when commands green. Decisions: agent ADR 0003 (Claude, delegated). Learning document: `docs/learning/phase-2/stage-3-forms-and-changes.md`.

## Context

Stage 2 answered questions. Stage 3 lets the customer act, with a person's click as the only way a change happens, forms for exact values, documents for loan applications, charts, diagrams and the old chats.

## Slices

1. Bank gaps: `StandingOrderEndpoints.ListAsync`, `AppointmentEndpoints.ListAsync`, `ApplicationQueue` for customers; tests in each service.
2. MCP: `CustomerChangeTools`, new reads in `CustomerTools`, `BankApi.ChangeAsync/UploadAsync`; tests `CustomerChangeToolsTests`.
3. Host: `ConfirmFirst`, `ChangeSummary`, `FormCheck`, `UiTools` (form, document), `ActionEndpoints`, `HostSession.Pending`, `BankToolSet` groups; migration `WidenMessageKind`; tests `ActionTests`, unit tests.
4. Host: `show_chart`, `show_diagram`, `ChatEndpoints`, `ChatStore.ListAsync`.
5. UI: `interactive.tsx`, `chart.tsx`, `diagram.tsx`, `History.tsx`, `App.tsx`; tests `interactive.test.tsx`, Playwright old chats.

## Done-when results (2026-11-03)

- `agent`: unit 54, MCP 20, integration 44 passed; chat-ui 30 passed, build OK; Playwright 6 passed (AppHost).
- Bank: accounts 63 + 75, customers 46 + 38, lending 1,577 + 66 passed.
- Live MCP check on the AppHost: see the learning document.

## Blocking questions

None. Transfers to other customers' accounts are out of scope: no API lets a customer find another person's account (not invented).
