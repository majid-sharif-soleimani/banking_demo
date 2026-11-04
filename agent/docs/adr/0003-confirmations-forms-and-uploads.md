# 0003. Confirm changes, take forms and uploads through host endpoints outside the model

- Status: Accepted; changed by ADR 0007 (an `unknown` outcome keeps the change pending; the upload tool is `host_document_upload`)
- Date: 2026-11-03
- Scope: agent (Bank.Agent.Host, Bank.Agent.Mcp, chat-ui), bank-accounts, bank-customers, bank-lending
- Design plan reference: `phase2-design-plan.md` decisions A8, A11, flow 4.3, sections 5 and 8; agent ADR 0002
- Decided by: Claude, on Majid's delegation (2026-11-03).

## Context

Stage 3 lets the model ask for changes that move money or book things. Decision A11 says a person confirms every change and the host, not the model, enforces it. Forms must return structured values, and loan applications need documents. The design had to stay simple and keep every person's action out of the model's reach.

## Decision

1. **Which tools need confirmation comes from MCP.** Every change tool is `ReadOnly = false` in the MCP server; the host wraps each such tool in `ConfirmFirst` before the model sees it. Calling it stores the tool and its exact arguments in the host session (`PendingChange`, random id) and shows a confirmation card built from those arguments (`ChangeSummary`). The model is told to wait.
2. **The person answers through plain host endpoints.** `POST /agent/changes/{id}/confirm|cancel`, `POST /agent/forms/{id}`, `POST /agent/documents/{id}`. They need the session cookie and `X-Bank-Chat`, find the pending item in *this* session, answer it once, store the outcome as a chat message, and return it. The UI then sends a short message ("I confirmed the change.") so the agent continues with the outcome in its history.
3. **The confirmation id is the bank's idempotency key.** The host opens the MCP connection for a confirmed change with `Idempotency-Key: <id>`; the MCP server refuses any change without that header. A double click makes one change; a change the model calls directly makes none.
4. **Forms are checked by the host.** `FormCheck` checks the definition the model sends and the values the person sends (required, number limits, dates, choices). Wrong values keep the form open with an error per field.
5. **Uploads go host → MCP → Customers.** The file (PDF, PNG, JPEG, at most 5 MB) goes to the host-only tool `document_upload` as base64; the model never gets that tool.
6. **Charts are our own SVG, diagrams are Mermaid.** At most four series, colours from a colour-blind-checked palette, a hover title per mark and a table of the numbers. Mermaid loads on first use with `securityLevel: "strict"`.
7. **Old chats** are `GET /chats` and `GET /chats/{id}`, read through `ChatAccess.CanRead`; reads of another person's chat are logged with the reader.
8. **Bank gaps** (section 8): three per-customer lists; transfers go between the customer's own accounts, since no API lets a customer look up another person's account.

## Rationale

The model cannot press a button: confirmation, form values and files arrive over endpoints only the browser calls, tied to the session. Storing the arguments at call time means the card shows exactly what the bank will receive. Using MCP's read-only flag keeps the rule in one place (the tool's definition). The follow-up chat message reuses the normal run, so there is no second way for the agent to continue.

## Alternatives considered

- AG-UI frontend tools (the browser returns the tool result in the next run): standard, but the host would have to trust a client-written tool result and match it to a pending call inside a run; our endpoints are smaller and easier to test.
- Microsoft.Extensions.AI approval requests (`ApprovalRequiredAIFunction`): ties confirmation to the agent loop's resume, which our server-side history does not keep.
- Confirmation by typing "yes": the model would decide what "yes" means.
- A list of change tools in the host: duplicates the MCP definitions and drifts.
- A chart library: one more dependency for two chart kinds.

## Consequences

- Pending items live in the host session: a restart or a session end drops them (the person asks again).
- The model sees a card number from a form when paying by card (test cards only).
- Each confirmed change opens one extra MCP connection.
- An admin's changes (stage 4) reuse the same path.
