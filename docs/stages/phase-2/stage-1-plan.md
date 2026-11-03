# Phase 2, stage 1 plan: Chat shell and login

Spec: `docs/stages/phase-2/stage-1.md`. Status: waiting for Majid's D1 decisions (below).

## Context

Phase 2 puts an agent and a chat UI in front of the bank. Before any language model is involved, this stage builds the frame that keeps it safe: a login the model never sees, a bank token that never leaves the agent host, a new chat per login, and a clear end when the bank session ends. The echo agent stands in for the model, so stage 2 only swaps the agent behind the same AG-UI endpoint.

## Slices

### 1. Agent host and the `agent` schema

- Projects: `agent/src/Bank.Agent.Host`, `agent/tests/Bank.Agent.UnitTests`, `agent/tests/Bank.Agent.IntegrationTests`; `agent/CLAUDE.md`.
- AppHost: resource `agent`, references the gateway and PostgreSQL; database role and schema `agent` added the same way as the bank services (bank-platform ADR 0001).
- Files: `Data/AgentDbContext.cs`, `Data/Entities.cs` (`Chat`, `ChatMessage`), first migration, `Program.cs` with health and the gateway `HttpClient`.
- Tests first: integration test that the migration creates both tables in schema `agent`; platform test that `agent` is healthy.
- Who writes: Claude.
- D1: none new (the schema follows ADR 0001 and decision A10).

### 2. Login, logout and the server session

- Files: `Features/Auth/AuthEndpoints.cs`, `Features/Auth/HostSessions.cs` (store), `Features/Auth/SessionCookie.cs`, `Features/Auth/BankIdentityClient.cs`, `Logic/SessionEnd.cs`.
- Tests first: `SessionEnd` unit tests; integration tests for login, cookie flags, wrong login, bank down, second login, logout, custom header, no secret in database or logs.
- Who writes: Claude.
- D1: where host sessions live (memory or PostgreSQL).

### 3. Chat storage and the AG-UI echo endpoint

- Files: `Features/Chats/ChatStore.cs`, `Features/AgUi/EchoAgent.cs`, `Features/AgUi/AgUiEndpoint.cs`, `Logic/ChatAccess.cs`.
- Tests first: `ChatAccess` unit tests, integration tests for an echo run, stored messages, and a run after the bank session ended.
- Who writes: Claude.
- D1: none new.

### 4. Chat UI

- Folder `agent/chat-ui/`: Vite, React, TypeScript; login form, chat view, banner, logout.
- Tests first: component tests for the login form (values cleared after submit) and the session-ended banner.
- Who writes: Claude.
- D1: the chat library (and whether a Node runtime is needed); how the UI is served.

### 5. End-to-end check in a browser

- Folder `agent/tests/chat-ui-e2e/`: Playwright against the AppHost.
- Who writes: Claude.
- D1: none.

## ADRs

- Workspace ADRs for phase 2 design decisions A7–A13 (`phase2-design-plan.md` section 7.1): Claude writes them (Majid handed all phase 2 writing to Claude on 2026-11-03).
- One ADR per D1 decision of this stage, in `agent/docs/adr/`.

## Done-when commands

See the spec.

## Blocking questions

None. The two library facts below were checked on 2026-11-03 and shape the first D1 decision:

- CopilotKit's React provider normally talks to a CopilotRuntime server (Node). A direct connection to an AG-UI endpoint exists but is marked development only (`agents__unsafe_dev_only`).
- assistant-ui's `@assistant-ui/react-ag-ui` connects directly to any AG-UI endpoint through `HttpAgent` from `@ag-ui/client`.
