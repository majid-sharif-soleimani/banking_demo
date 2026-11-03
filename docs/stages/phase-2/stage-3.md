# Phase 2, stage 3: Forms, diagrams and changes

Goal: the customer can change things through the chat, safely. The agent asks for exact values with a form, shows a confirmation card with the exact values before every change, and runs the change only after the person presses Confirm. It also shows charts and diagrams, takes documents for a loan application, and the person can read their old chats.

Read first: `phase2-design-plan.md` sections 2.2 (the "Medium" customer rows), 4.3, 5, 7.1 (A8, A11) and 7.3 (questions 1–3, 6), 8; agent ADR 0002.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Human in the loop (confirmation outside the model), idempotency keys, forms as structured input, validating on the server, file uploads, read-only history | None asked (delegated, 2026-11-03). Claude's choices: agent ADR 0003. | Nothing |

Slices: (1) bank gaps (section 8), (2) the change tools in the MCP server, (3) confirmation, forms and uploads in the host, (4) charts, diagrams and the old chats, (5) the chat UI.

## Bank gaps (section 8)

- Accounts `GET /standing-orders?customerId=`; Customers `GET /appointments?customerId=`; Lending: a customer's own page of `GET /loan-applications`. Another customer's id gives an empty list.
- `GET /information-requests` already shows a customer only their own requests (checked, no change).

## MCP server

- New reads: `standing_orders_list`, `loan_simulation`, `payoff_quote`, `loan_applications_list`, `appointments_list`, `appointment_slots`.
- Changes (`ReadOnly = false`): `transfer_create` (between own accounts), `standing_order_create`, `standing_order_cancel`, `card_payment`, `payoff_request`, `appointment_book`, `appointment_cancel`, `loan_application_submit`, and the host-only `document_upload`.
- A change runs only with an `Idempotency-Key` on the MCP connection; the host sets it to the confirmation's id.

## Agent host

- A change tool reaches the model behind `ConfirmFirst`: calling it stores the call in the session and shows `show_confirmation`. `POST /agent/changes/{id}/confirm|cancel` answers it once.
- UI tools: `show_chart`, `show_diagram`, `show_form` (answered by `POST /agent/forms/{id}`), `request_document` (answered by `POST /agent/documents/{id}`, PDF/PNG/JPEG up to 5 MB).
- `GET /chats`, `GET /chats/{id}`: own chats for a customer, every chat for the admin; reads of another person's chat are logged.
- New message kinds; the model sees actions as short notes in later turns.

## Tests (minimum)

- Unit: change summaries, form definition and value checks, chart and diagram checks.
- MCP: change tools refused without a key, body and key forwarded, upload as multipart, simulation needs no key, optional arguments.
- Integration: a change only after Confirm, once, with the key; cancel; another session cannot confirm; cross-site post refused; form values checked and passed on; upload; old chats per person; a bad chart refused.
- Bank: the three new lists.
- Chat UI: confirmation, form, upload, chart, stored messages. Browser: the old chats.

## Done when

```bash
(cd agent && dotnet build && dotnet test)
(cd agent/chat-ui && npm ci && npm test && npm run build)
(cd agent/tests/chat-ui-e2e && npm ci && npx playwright test)   # with the AppHost running
(cd core_banking/bank-accounts && dotnet test); (cd core_banking/bank-customers && dotnet test); (cd core_banking/bank-lending && dotnet test)
```
