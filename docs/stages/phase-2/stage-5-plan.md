# Phase 2, stage 5 plan: Complex workflows

Spec: `docs/stages/phase-2/stage-5.md`. Status: built 2026-11-03; done-when commands green. Decisions: agent ADR 0005 (Claude, delegated). Learning document: `docs/learning/phase-2/stage-5-workflows.md`.

## Context

Stages 2 to 4 gave each role small tools. The complex workflows are mostly the model combining them, guided by the instructions. Two things were missing: a bank list of the overdue loans themselves, and a safe way to let the agent use a customer's document, which is the main door for prompt injection.

## Slices

1. Bank: `LoanQueries.ByClassificationAsync`; test in `DisbursementTests`. Commit `feat(bank): ...`.
2. MCP: `AdminTools` (`loans_by_classification`, `customer_document`), `AdminChangeTools` (`customer_note_add`, `appointment_book_for_customer`), `SharedTools.appointment_slots`, `BankApi.GetFileAsync`; tests in `AdminToolsTests`.
3. Host: `Logic/UntrustedContent`, `Logic/DocumentFacts`, `Features/Agent/MarkUntrusted`, `Features/Agent/DocumentReader`, wiring in `AgUiEndpoint`; `ToolResult` unwraps string results; tests in `UntrustedContentTests`, `AgentToolTests`.
4. Instructions: `AgentInstructions` (both roles); tests in `AgentInstructionsTests`.

## Done-when results (2026-11-03)

- `agent`: unit 68, MCP 38, integration 48 passed.
- Bank: lending 1,577 + 67 passed.
- Live check on the AppHost: see the learning document.

## Blocking questions

None. The bank sends no free-text message from the chat (no such API); arrears follow-up drafts the message for the admin and records a note.
