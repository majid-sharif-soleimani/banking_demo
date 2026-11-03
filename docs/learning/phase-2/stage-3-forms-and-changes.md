# Phase 2, stage 3: forms, confirmations and changes

Same order as every learning document: the big picture first, then the parts and how one change travels through them, then why. Tests, limits and questions at the end.

---

## Part 1. The big picture

### 1.1 The goal

In stage 2 the customer could only ask. Now they can act through the chat: move money between their accounts, create or cancel a standing order, pay a loan installment with a test card, pay off a loan early, book or cancel an appointment, and apply for a loan with documents. The chat also shows charts and diagrams, and the customer can read their old chats.

### 1.2 The one idea to understand first: human in the loop

A language model can misunderstand. "Move 250 to savings" could become 2 500, or the wrong account. So the rule (decision A11) is: **the model may ask for a change, but only a person's click makes it happen**, and the host enforces that, not the model.

```
model:   "call transfer_create(from, to, 250.00 SEK)"
host:    does NOT call the bank; stores the call; shows a card:  Transfer money · Amount 250.00 · Currency SEK · [Confirm] [Cancel]
person:  clicks Confirm  →  browser → host endpoint (not the model)
host:    runs the stored call once → bank → stores the outcome → the agent continues
```

The card shows the values the tool will really send, taken from the call itself, not the model's sentence about it.

### 1.3 Why it is hard

- **The model must not be able to confirm.** If confirmation were "type yes", the model (or a prompt injection) could produce the yes.
- **Double clicks and retries.** A confirmation that arrives twice must not move money twice.
- **Exact input.** Free text like "about two hundred" is not an amount. Forms give typed values, but a browser can send anything, so the server checks them.
- **Files.** A loan application needs payslips. Files must reach the bank without passing through the model.

---

## Part 2. The building blocks and how they connect

### 2.1 One change, end to end

```mermaid
sequenceDiagram
  participant B as Browser
  participant H as Agent host
  participant M as Model
  participant T as MCP server
  participant G as Bank
  B->>H: "Move 250 SEK to my savings"
  H->>M: tools (transfer_create behind ConfirmFirst)
  M-->>H: call transfer_create(...)
  H->>H: ConfirmFirst: store PendingChange(id, tool, args) in the session
  H-->>B: show_confirmation {changeId, title, fields}
  H->>M: "Not done yet; tell them to press Confirm"
  M-->>B: "Please check the values and press Confirm."
  B->>H: POST /agent/changes/{id}/confirm
  H->>T: transfer_create(args), header Idempotency-Key: id
  T->>G: POST /api/accounts/transfers (Idempotency-Key: id)
  G-->>T: receipt
  T-->>H: receipt
  H->>H: store change_result
  H-->>B: {status: done}
  B->>H: run "I confirmed the change."
  H->>M: history now ends with "(My change result: done, receipt...)"
  M-->>B: "Done. 250.00 SEK moved." + receipt card
```

### 2.2 Slice 1: the bank's missing lists

Three small read endpoints from design plan section 8, each like `GET /accounts?customerId=`: another customer's id gives an empty list.

- Accounts `GET /standing-orders?customerId=` (`StandingOrderEndpoints.cs`).
- Customers `GET /appointments?customerId=` (`AppointmentEndpoints.cs`).
- Lending `GET /loan-applications`: staff see the queue; a customer now sees only their own applications (`ApplicationQueue.cs`).

### 2.3 Slice 2: change tools in the MCP server (`CustomerChangeTools.cs`, `BankApi.cs`)

Each change tool is marked `ReadOnly = false`. That flag is the single source of "this needs confirmation". `BankApi.ChangeAsync` refuses to run without an `Idempotency-Key` header on the MCP connection, so even a direct call by the model would do nothing. Money goes out as the bank expects it: `{ "amount": "250.5", "currency": "SEK" }`. `document_upload` is a change too, but only the host calls it.

### 2.4 Slice 3: confirmation, forms and uploads in the host

- `Features/Agent/ConfirmFirst.cs`: wraps a change tool. Same name and parameters for the model; calling it stores a `PendingChange` in `HostSession.Pending` and queues a `show_confirmation` card.
- `Logic/ChangeSummary.cs`: the card's title and lines ("fromAccountId" → "From account id").
- `Features/Agent/UiTools.cs`: `show_form` stores an `OpenForm`; `request_document` stores a `DocumentRequest`. Both wait for the person.
- `Logic/FormCheck.cs`: checks a form's definition and the person's values.
- `Features/AgUi/ActionEndpoints.cs`: the four endpoints. Each checks the session like a run, takes its pending item out of *this* session (so it can be answered once, and only by this browser), acts, stores the outcome (`change_result`, `form_values`, `document`) and returns it.
- `Features/Agent/BankTools.cs`: tools come in three groups (reads, changes, host-only). For a confirmed change the connection carries `Idempotency-Key: <confirmation id>`.

**How the agent continues.** After an action the UI sends a short message ("I confirmed the change."). The next run's history contains the stored outcome as a note before it, so the model can say what happened.

### 2.5 Slice 4: charts, diagrams and old chats

- `show_chart`: our own SVG (`chat-ui/src/chart.tsx`), line or bar, up to four series with colour-blind-checked colours, a hover title on every mark, a legend for two or more series and "Show the numbers" as a table.
- `show_diagram`: Mermaid (`chat-ui/src/diagram.tsx`), loaded only when a diagram appears, with `securityLevel: "strict"`.
- `Features/Chats/ChatEndpoints.cs`: `GET /chats` (own chats, every chat for the admin) and `GET /chats/{id}` (read-only). An admin reading someone else's chat is logged.

### 2.6 Slice 5: the chat UI

`interactive.tsx` holds the three answered components (form, confirmation, upload). Each posts to its host endpoint, shows the outcome, and calls `onDone`, which appends the follow-up message. `History.tsx` lists old chats and draws each stored message by its kind, read-only.

---

## Part 3. Why it is built this way

Claude's choices on Majid's delegation, recorded in agent ADR 0003. They build on Majid's A8 (UI components as tools) and A11 (every change confirmed, enforced by the host).

- **Endpoints outside the model.** The model has no tool that confirms. Rejected: typing "yes" (the model decides what yes means); AG-UI frontend tools returning the result in the next run (the host would trust a client-written result).
- **One rule from MCP's read-only flag.** Rejected: a list of change tools in the host (would drift).
- **Confirmation id = idempotency key.** The bank already requires a key for money movements; reusing the id makes double clicks harmless.
- **Pending items in the session.** Simple, and they end with the session. Price: a restart drops them.
- **Own SVG chart.** Two chart kinds do not need a library.

Patterns: **human in the loop**, **idempotency key**, **server-side validation**, **decorator** (`ConfirmFirst` around a tool), **capability from metadata** (the read-only flag).

---

## What was tested

| Project | Scenarios | Result |
| --- | --- | --- |
| `Bank.Agent.UnitTests` | change titles and labels; form definitions and values; chart and diagram checks | 54 passed |
| `Bank.Agent.Mcp.Tests` | 20 tools listed by read-only flag; change refused without key; key and money body forwarded; application for the connection's customer; cancel as DELETE; upload as multipart; simulation without key; optional arguments | 20 passed |
| `Bank.Agent.IntegrationTests` | stage 2's 35 plus: change only shown; confirm runs once with the key; outcome in the next turn; cancel; another session and a cross-site post refused; form checked and passed on; upload and wrong file; old chats per person with a log line; bad chart | 44 passed |
| `chat-ui` (Vitest) | confirmation (confirm, refused, cancel, read-only), form (values, errors), upload, chart (bars, line, legend), stored messages | 30 passed |
| `chat-ui-e2e` (Playwright, AppHost) | stage 1's five plus the old chats | 6 passed |
| Bank | standing orders, appointments, own applications | accounts 63 + 75, customers 46 + 38, lending 1,577 + 66 passed |

Live check on the AppHost (2026-11-03): through the MCP server with a real customer token, a transfer without a key was refused, with a key it moved 250.50 SEK, and the same key again returned the same transfer; a standing order, a simulation and a loan application worked, and the customer's own application list showed it.

## What to watch later

- Nothing here was tried with the real model yet (needs Majid's key): how well Claude fills forms and waits for Confirm is untested.
- Mermaid diagrams were not seen in a real browser under the host's Content Security Policy.
- The card number from a form reaches the model (test cards only).
- Pending items die with a restart.

## Check your understanding

1. Which two independent checks stop a change that the model calls without the person's click?
2. Why does a double click on Confirm move the money only once?
3. Where are a form's values checked, and why there and not only in the browser?
4. Why can another browser that knows the change id not confirm it?
5. Why does the UI send "I confirmed the change." after the click?
