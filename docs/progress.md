# Phase 1 Progress

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

## Open questions

- 2026-10-31: Majid delegated D1 decisions for stages 2–8 to Claude (no questions). Resolved 2026-11-02: every stage was reviewed (stage 5 accepted without review) and merged into `master`; each stage plan has a sign-off section.

- Account number format decided on 2026-10-30 (9 digits: 8 random plus Luhn). Its ADR is still to be written by Majid (workspace `docs/adr/0003`).

- 2026-11-02: stage 3 changed `Allocation.Apply` (new `LoanTerms` parameter, prepayment of later items) and the early payoff accrual. The chained branches `stage-4-customers-accounts` … `stage-8-e2e-aws` were built on the old stage 3; the stage 5 payment handler still calls the old signature and says "never prepaid". Bring them up to date before reviewing stage 4 and later. Done for `stage-4-customers-accounts` on 2026-11-02 (master merged in, then merged into master); stages 5–8 done the same way on 2026-11-02. Phase 1 complete.

- 2026-11-02, before the first AWS deployment: Majid reviews the CDK stack with `/explain`, then runs `cdk deploy`, `aws` commands and image pushes himself.
- 2026-11-02: data import column mapping (`Import:Columns`) to check against the real databasesample.com export before the first real run.
- Phase 2 (the agent) starts only when Majid says so.
