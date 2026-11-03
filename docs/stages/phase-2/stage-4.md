# Phase 2, stage 4: The admin role

Goal: the bank's admin works through the same chat. The agent reads the bank's books and reports, finds a customer and shows their whole picture, shows the work waiting for review, and makes the admin's changes (activate a currency, close the day, review, approve, reject, pay out, ask for documents) only after the admin's own click.

Read first: `phase2-design-plan.md` sections 2.2 (the "Simple" and "Medium" admin rows, and "Loan application review"), 6, 7.3 (question 1), 9 (step 4); agent ADRs 0002 and 0003.

## Learning

| Concepts | Decisions for Majid (D1) | Majid writes |
| --- | --- | --- |
| Least privilege per role, one tool set per MCP path, defence in depth (the bank checks again), a person decides a loan, not the model | None asked (delegated, 2026-11-03). Claude's choices: agent ADR 0004. | Nothing |

Slices: (1) bank fixes: staff-only checks and the open information request queue, (2) the admin tools in the MCP server, (3) the host opens the admin's tools and gives the admin their instructions.

## Bank fixes (phase 1 bugs found here)

- Operations: closing the day and activating a currency refuse a customer's call (403 `staff_only`). Before, a customer's token could close the business day.
- Ledger: `GET /journal-entries` and `GET /trial-balance` refuse a customer's call (403 `staff_only`).
- Customers: `GET /information-requests` without `applicationId` is the staff queue of every open request, oldest first (at most 100).

## MCP server

- `/mcp/admin` next to `/mcp/customer`. `ToolSets` keeps, per request, only the tools of the path; `SharedTools` (`business_date`, `loan_products_list`) are on both.
- Reads: `currencies_list`, `trial_balance`, `journal_entries`, `portfolio_report`, `arrears_report`, `customers_search`, `customer_get`, `customer_accounts`, `customer_loans`, `customer_interactions`, `customer_appointments`, `customer_standing_orders`, `loan_applications_queue`, `loan_application_get`, `information_requests_open`, `information_requests_of_application`.
- Changes (`ReadOnly = false`, so behind the confirmation): `currency_activate`, `business_day_close`, `loan_application_start_review`, `loan_application_approve`, `loan_application_reject`, `loan_application_disburse`, `loan_application_request_information`, `information_request_complete`.

## Agent host

- `IBankTools.OpenAsync` picks the path by the session's role: the admin connects to `/mcp/admin` with the admin's token and no customer id.
- The admin's instructions: what the tools do, the trial balance check, the customer overview, closing the day with checks, the review rule (the agent never decides or recommends a loan; approve or reject only on the admin's word).
- Confirmation titles for the admin's changes. The list of every chat came in stage 3.

## Tests (minimum)

- Bank: a customer cannot close the day, activate a currency or read the books; the open request queue for staff, 403 for a customer, 401 without an actor.
- MCP: the admin path lists only admin and shared tools; the customer path is unchanged after an admin request; a customer connection cannot call an admin tool; admin reads hit the right bank paths; search is a POST; a change without a key is refused; a rejection sends the reason and the key.
- Integration: the admin's run gets the admin tools with no customer id; an approval waits for the admin's click and runs once with the key.

## Done when

```bash
(cd agent && dotnet build && dotnet test)
(cd agent/tests/chat-ui-e2e && npm ci && npx playwright test)   # with the AppHost running
(cd core_banking/bank-operations && dotnet test); (cd core_banking/bank-ledger && dotnet test); (cd core_banking/bank-customers && dotnet test)
```
