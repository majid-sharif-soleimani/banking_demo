# 0004. Give the admin their own MCP path and tool set, with the same confirmation rule

- Status: Accepted
- Date: 2026-11-03
- Scope: agent (Bank.Agent.Mcp, Bank.Agent.Host), bank-operations, bank-ledger, bank-customers
- Design plan reference: `phase2-design-plan.md` sections 2.2 (admin rows), 6 (least privilege), 7.3 question 1, 9 step 4; agent ADRs 0002 and 0003
- Decided by: Claude. Majid delegated the phase 2 decisions on 2026-11-03.

## Context

The admin needs reports, the business day, currencies, any customer's data, the loan review queue and the changes that go with them. Three things had to be chosen: how the admin's tools are kept away from the customer, how the admin's tools name a customer, and how the admin's changes are confirmed. Matching the tools against the bank also showed that some staff endpoints accepted a customer's token.

## Decision

1. **One MCP server, one path per role.** `/mcp/customer` and `/mcp/admin` on the same server. Every tool class is registered once; `ToolSets.KeepToolsOfPath` (the transport's `ConfigureSessionOptions`, called per request in stateless mode) removes the tools that do not belong to the path. `business_date` and `loan_products_list` are shared.
2. **The host picks the path from the session's role.** The admin connects with their bank token and no `X-Customer-Id`. The model cannot choose the path.
3. **Admin tools take a customer id as a parameter.** The admin works on many customers. Safety comes from the bank: the gateway turns the admin's token into `X-Performed-By`, and staff endpoints refuse a customer's token.
4. **The bank's missing staff checks are fixed in the bank**, not only hidden by the tool set: Operations (close day, activate currency) and Ledger (journal, trial balance) return 403 `staff_only` for a customer.
5. **The same confirmation rule as the customer (ADR 0003).** Admin changes are `ReadOnly = false`, so they reach the model behind `ConfirmFirst`. Approving and rejecting a loan is the admin's click; the instructions forbid the agent to decide or recommend.
6. **The open information request queue is a bank endpoint** (`GET /information-requests` without `applicationId`, staff only, oldest 100), not a loop over applications in the agent.

## Rationale

Two paths on one server keep one deployment and one set of tool code, while a customer's agent never even sees the admin tools' names. Choosing by role in the host keeps the model out of the decision. Fixing the bank closes the hole for every client, not just the agent: the tool set is the first line, the bank the second.

## Alternatives considered

- Two MCP servers (customer and admin): stronger separation, but a second deployment and duplicated plumbing for the same gateway client.
- One path with every tool and a role check inside each tool: the model would see every tool, and each tool would need the check.
- Filtering in the host instead of the server: the server would still offer admin tools to any client that asks.
- A "customer 360" tool that makes five bank calls: fewer model steps, but business logic in the MCP server; the model composes the five reads instead.

## Consequences

- A new tool must be put in the right class; the test listing each path's tools catches a wrong place.
- The approval card shows the application id; the admin relies on the application card the agent showed before. A richer card (customer and amount) can come later.
- Customer texts in interactions reach the admin's model; the instructions treat them as data. Stage 5 adds explicit untrusted-content handling for documents.
