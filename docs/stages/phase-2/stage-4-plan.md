# Phase 2, stage 4 plan: The admin role

Spec: `docs/stages/phase-2/stage-4.md`. Status: built 2026-11-03; done-when commands green. Decisions: agent ADR 0004 (Claude, delegated). Learning document: `docs/learning/phase-2/stage-4-admin.md`.

## Context

Stages 2 and 3 served the customer. Stage 4 gives the admin a tool set of their own, on its own MCP path, with the same confirmation rule. While matching the admin tools against the bank, three staff-only checks turned out to be missing in phase 1; they are fixed first.

## Slices

1. Bank: `Features/StaffOnly.cs` in Operations and Ledger, guards in `CloseBusinessDay`, `ActivateCurrency` and `Reports`; the open queue in Customers `InteractionEndpoints.RequestsAsync`; tests in each service. Commit `fix(bank): ...`.
2. MCP: `SharedTools`, `AdminTools`, `AdminChangeTools`, `ToolSets` (`ConfigureSessionOptions`), second `MapMcp`; tests `AdminToolsTests`.
3. Host: `IBankTools.OpenAsync` by role, admin instructions, `ChangeSummary` titles; tests in `AgentToolTests`, `ActionTests`, `AgentInstructionsTests`.

## Done-when results (2026-11-03)

- `agent`: unit 54, MCP 35, integration 45 passed; Playwright 6 passed (AppHost).
- Bank: operations 17 + 46, ledger 22 + 8, customers 46 + 39 passed.
- Live check on the AppHost: see the learning document.

## Blocking questions

None. The confirmation card of an approval shows the application id, not the customer's name and amount; the agent shows the application card first (noted in "What to watch later").
