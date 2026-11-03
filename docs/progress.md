# Progress

Phase 1 is complete (closed 2026-11-03). It is reopened only to fix bugs. Phase 2 (the agent) started on 2026-11-03.

## Phase 2

| Step | Name | Status | Closed on | Notes |
| --- | --- | --- | --- | --- |
| 0 | Design and requirements | Done | 2026-11-03 | `phase2-design-plan.md`: decisions A1–A13 and the business answers in section 7 made by Majid; workspace ADR 0008 |
| 1 | Chat shell and login | Done | 2026-11-03 | Agent host (BFF, in-memory sessions, sweeper, schema `agent`, AG-UI echo run) and chat UI (assistant-ui, fits phone and desktop); agent ADR 0001, workspace ADR 0008. Unit 23, integration 27, chat-ui 15, Playwright 5 (AppHost), platform 54 green. Review: 1 REQUIRED (chats left open after a closed tab or a restart) fixed; OPTIONAL fixed: concurrent runs, bank outage in the UI, secrets in record ToString; open: half turns on disconnect, timeouts in code, PerformedBy on close, run vs login race (stage 2). Retro skipped (Majid: no questions in phase 2). Live on the home bank (127.0.0.1:8090, Funnel on tailnet port 8443). Merged into `master` |
| 2 | First agent, customer reads | Done | 2026-11-03 | Bank MCP server (`Bank.Agent.Mcp`, five customer read tools, token and customer id as connection headers), Claude via Anthropic SDK behind `IChatClient` (echo without a key), `ChatClientAgent` per run, `show_table`/`show_card` as server-side UI tools, `chat_message.kind`; agent ADR 0002 (Claude, delegated). Unit 34, MCP 10, integration 35, chat-ui 20, Playwright 5, platform 55, end-to-end 5 green. Not yet on the home bank (needs Majid's Anthropic key). Not merged |
| 3 | Forms, diagrams and changes | Done | 2026-11-03 | Every customer change behind a host-enforced confirmation (`ConfirmFirst`, MCP read-only flag, confirmation id as idempotency key), forms checked by the host, document uploads, charts (own SVG), Mermaid diagrams, old chats (`GET /chats`). Bank gaps: standing orders, appointments, own loan applications. Agent ADR 0003. Unit 54, MCP 20, integration 44, chat-ui 30, Playwright 6; accounts, customers, lending green. Not merged |

## Phase 1

Updated by `/close-stage`. One row per stage from section 10 of `phase1-design-plan.md`. Statuses: Not started, In progress, Ready for review, Done. The project runs in learning mode (`docs/implementation-decisions.md`); what was learned per stage is in `docs/learning-log.md`.

| Stage | Name | Status | Closed on | Notes |
| --- | --- | --- | --- | --- |
| 0 | Design and requirements | Done | 2026-10-29 | All assumptions in section 11 confirmed |
| 1 | Foundation: contracts, messaging, platform | Done | 2026-10-30 | Slices 1.1 to 1.4 done; 179 + 27 tests green; review fixes in, accepted demo limits in implementation-decisions |
| 2 | Operations | Done | 2026-11-02 | Slices 2.1–2.4 done; 17+44 Operations, 29 platform, 185 contracts green (re-run 2026-11-02); ADRs ops 0001 and contracts 0016 confirmed by Majid; retro done; merged into master |
| 3 | Lending pure logic (TDD) | Done | 2026-11-02 | Slices 3.1–3.5; 1,498 tests green (re-run 2026-11-02); ADRs lending 0001–0003 reviewed by Majid (0003 point 5 changed to prepayment, point 9 nets prepaid interest); retro skipped; merged into master |
| 4 | Customers and Accounts | Done | 2026-11-02 | Slices 4.1–4.5; customers 37, accounts 115, platform 33, contracts 221 green (re-run 2026-11-02 after merging master); accounts ADRs 0001–0003 and contracts 0017 confirmed by Majid; retro skipped; merged into master |
| 5 | Lending service | Done | 2026-11-02 | Slices 5.1–5.6; lending 1,612, customers 58, accounts 120, operations 61, platform 35 green (re-run 2026-11-02 after merging master); handler on the new Allocation.Apply; ADRs lending 0004–0005 accepted by Majid without review; retro skipped; merged into master |
| 6 | Cards and Ledger | Done | 2026-11-02 | Slices 6.1–6.4; cards 13, ledger 28, lending 1,621, customers 58, accounts 120, operations 61, platform 39 green (2026-11-02 after merging master); lending 0006 changed by Majid to movement projections per report, payments during a pending payoff unallocated; ledger 0001–0002 confirmed; retro skipped; merged into master |
| 7 | Gateway and data import | Done | 2026-11-02 | Slices 7.1–7.5; gateway 9, data-import 34, platform 47 green (2026-11-02 after merging master); data-import 0001 confirmed by Majid; column mapping still to check against the real export; retro skipped; merged into master |
| 8 | End-to-end and AWS | Done | 2026-11-02 | Slices 8.1–8.5; end-to-end 5/5, cdk synth and compose config OK (2026-11-02 after merging master); platform 0002 and 0004 confirmed, 0003 changed by Majid (e2e on every push to master); not deployed, CDK review with /explain before the first deploy; retro skipped; merged into master |

## Hosting

- 2026-11-03: the bank runs on a private host machine by `core_banking/bank-platform/deploy/home-hosting.md` (Docker Compose, images tagged `home`). Platform 47 and end-to-end 5 green before the build. Loaded with generated synthetic data (option C: 100 customers, 363 business days, verification passed; current business date 2026-12-30). Step 7 checks pass locally and through Tailscale. Published with Tailscale Funnel (open to the internet); public relays served it about 3 minutes after `funnel` was turned on. Reboot check still to do.
- 2026-11-03: the gateway asks every `/api` caller for a shared key in `X-Api-Key` (`Gateway__ApiKey`; at home in the gitignored `deploy/env/gateway.env`, on AWS `GATEWAY_API_KEY` in the stack secret). Outside Development it refuses to start without one. Gateway tests 16 green; live on the home bank. Tailscale to move from Funnel to serve (tailnet only). The identity headers are still taken as given: real per-customer authentication waits for phase 2.

## Phase 1 extensions

- 2026-11-03: branch `feat-agent-read-apis` (not merged into `master`): four read APIs for the phase 2 agent. Lending `POST /loan-products/{code}/simulation` and `GET /loan-applications` (review queue); Customers `POST /customers/search`; Accounts `POST /transactions/search`. Keyset paging, POST for personal-data searches, staff-only reads (workspace ADRs 0004–0006; customers 0001, accounts 0004), decided by Claude on Majid's delegation except keyset (Majid). New indexes: `ix_loan_application_queue`, `ix_account_entry_business_date_sequence`. Tests green: lending 1,577 + 65, customers 45 + 36, accounts 63 + 74, platform 47, gateway 16. No gateway change. Learning docs in `docs/learning/agent-read-apis/`. End-to-end tests not run.
- 2026-11-03: the read APIs run on the home bank. Database backed up first (pg_dump, outside the repository); images `bank-customers`, `bank-accounts` and `bank-lending` rebuilt from the branch and recreated with `up -d --wait` (no other container changed, no `down -v`). Both new indexes exist; data intact (10,385 entries, 100 customers). Smoke tests through the gateway: simulation (6,170.85 / 22,150.54), queue, customer search and transaction search with a second page all 200; customer caller 403; no key 401. `EXPLAIN ANALYZE` of a one-week SEK search ≥ 10,000: index scan on `ix_account_entry_business_date_sequence`, 0.22 ms.

- 2026-11-03: branch `feat-identity` (built on `feat-agent-read-apis`, neither merged into `master`): new service `bank-identity` with test logins and sessions (workspace ADR 0007, all choices by Majid; the steps after them delegated to Claude). `admin/admin` hard-coded; customers log in with the national id as username and password (lookup through the new `nationalId` filter of `POST /customers/search`); a new token per login; one session per user (the old one gets 401 `session_replaced` once); 5-minute sliding idle timeout; sessions in memory. The gateway drops caller identity headers, checks `Authorization: Bearer` with identity on every `/api` call and sets `X-Performed-By`/`X-Customer-Id` itself. Data import, platform tests and end-to-end tests log in. Design plan ADR row 13 and section 11 changed. Tests green: identity 22 + 15, gateway 22, customers 46 + 37, platform 49, end-to-end 5, data-import 33 + 1. Learning doc `docs/learning/identity/identity-service.md`.
- 2026-11-03: identity runs on the home bank. Database backed up first (pg_dump to `~/bank-backups/`, outside the repository); platform 49 and end-to-end 5 green; all eight images rebuilt from `feat-identity` and started with `up -d --wait` (no `down -v`). Business date still 2026-12-30, trial balance equal in SEK, EUR and USD. Locally and through Tailscale: no key 401, no token 401, wrong password 401, admin login 200, a second admin login ends the first (`session_replaced`). A customer logged in with the national id sees their own accounts, an empty list for another customer's id and 404 for another customer's profile, also with a forged `X-Customer-Id`; staff search as a customer is 403, also with a forged `X-Performed-By`.
- 2026-11-03: both branches merged into `master`. Phase 1 closed.

## Open questions

- 2026-10-31: Majid delegated D1 decisions for stages 2–8 to Claude (no questions). Resolved 2026-11-02: every stage was reviewed (stage 5 accepted without review) and merged into `master`; each stage plan has a sign-off section.

- Account number format decided on 2026-10-30 (9 digits: 8 random plus Luhn). Its ADR is still to be written by Majid (workspace `docs/adr/0003`).

- 2026-11-02: stage 3 changed `Allocation.Apply` (new `LoanTerms` parameter, prepayment of later items) and the early payoff accrual. The chained branches `stage-4-customers-accounts` … `stage-8-e2e-aws` were built on the old stage 3; the stage 5 payment handler still calls the old signature and says "never prepaid". Bring them up to date before reviewing stage 4 and later. Done for `stage-4-customers-accounts` on 2026-11-02 (master merged in, then merged into master); stages 5–8 done the same way on 2026-11-02. Phase 1 complete.

- 2026-11-02, before the first AWS deployment: Majid reviews the CDK stack with `/explain`, then runs `cdk deploy`, `aws` commands and image pushes himself.
- 2026-11-02: data import column mapping (`Import:Columns`) to check against the real databasesample.com export before the first real run.
- 2026-11-03, home bank: Customers has the fixed address `172.30.0.10` (bank-platform ADR 0004) inside the network's dynamic range. When other containers are recreated first, one of them can take that address and Customers fails with "Address already in use". Worked around by removing the accounts container and starting customers first. Lasting fix to decide: an `ip_range` for dynamic addresses that excludes `.10` (needs the network recreated, so every container restarts once).
