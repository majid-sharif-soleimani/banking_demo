# 0008. Put a React chat UI and a backend-for-frontend agent host in front of the gateway

- Status: Accepted
- Date: 2026-11-03
- Scope: workspace (new `agent/` folder, bank-platform)
- Design plan reference: `phase2-design-plan.md` sections 3, 4 and 7 (decisions A7–A13); design plan ADR row 24
- Decided by: Majid in the chat of 2026-11-03 (every numbered decision below); written by Claude on his delegation.

## Context

Phase 2 adds an AI agent that a person reaches through a chat, as a customer or as the admin. Three forces shape the design. The password and the bank token must never reach the language model, because anything in the model's context can be sent to the provider, repeated in an answer, or pulled out by a prompt injection. A model that misunderstands a sentence must not be able to move money on its own. And the answers are not only text: tables, cards, diagrams and forms need a contract between the agent and the UI.

## Decision

1. **Chat UI in React with an AG-UI client** (A7). AG-UI is the protocol Microsoft Agent Framework hosts, and React has the most ready chat components.
2. **UI components as tools** (A8). The agent shows a component by calling a UI tool (`show_table`, `show_form`, ...) whose arguments follow one JSON Schema per component. Form values come back as the tool's result.
3. **The bank token lives in the agent host** (A9). The agent host is a backend for frontend (BFF): the login form posts to it, it logs in at identity through the gateway, keeps the token in a server-side session, and gives the browser only an opaque `HttpOnly` cookie. Neither the browser nor the model sees the token.
4. **Chat history in PostgreSQL, schema `agent`** (A10). Same rules as a bank service: its own role and schema, `CreatedAt`, `PerformedBy` and `Version` on every table. No password or token is ever stored. Chats are kept forever in this version; a customer reads their own old chats and the admin reads all of them (design plan section 7.3, question 6).
5. **Every tool that changes data needs the person's confirmation** (A11), enforced by the agent host, not by the model. Read-only tools need none.
6. **A new chat per login** (A12). When a session ends, its chat is closed and kept; the next login opens a new, empty chat.
7. **Hosting on the host machine** (A13), in the bank's Docker Compose project. Only the chat UI is published through Tailscale; the gateway stays internal.

## Rationale

The BFF keeps the one secret that matters (the bank token) in one process the person cannot script and the model cannot read. Server-side history means the client cannot rewrite earlier turns. UI components as typed tools fit the AG-UI model and give checked data instead of parsing free text. Confirmation in the host is the only control a prompt injection cannot talk its way past.

## Alternatives considered

- Blazor or a hand-made page for the UI: rejected; chat streaming, generative UI and approvals would be ours to build.
- A declarative UI format such as A2UI, or JSON blocks inside the text: rejected; more than section 5 of the design plan needs, or unchecked.
- The token in the browser: rejected; any script on the page could read it, and it leaks easily into a chat.
- Chat history in memory: rejected by Majid; he wants old chats readable later.
- Confirmation only for money movements, or per tool: rejected; one rule with no exceptions is easier to trust.
- Continuing the old chat after a new login: rejected; whoever sits at the browser next would see it.
- The agent in the cloud calling the bank through Funnel: rejected; the gateway would have to stay public.

## Consequences

- The agent host is security-critical code: it owns login, the cookie, sessions and the confirmation step. It is tested for secrets in storage and logs.
- Chats hold personal data forever. Before real customers: a retention period, a deletion path, and an audit of the admin reading other people's chats.
- A second language (TypeScript) and a Node toolchain join the repository, only for the UI.
- Phase 2 stage 1 builds decisions 3, 4 and 6; the others arrive in later stages (design plan section 9).
