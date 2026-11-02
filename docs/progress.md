# Phase 1 Progress

Updated by `/close-stage`. One row per stage from section 10 of `phase1-design-plan.md`. Statuses: Not started, In progress, Ready for review, Done. The project runs in learning mode (`docs/implementation-decisions.md`); what was learned per stage is in `docs/learning-log.md`.

| Stage | Name | Status | Closed on | Notes |
| --- | --- | --- | --- | --- |
| 0 | Design and requirements | Done | 2026-10-29 | All assumptions in section 11 confirmed |
| 1 | Foundation: contracts, messaging, platform | Done | 2026-10-30 | Slices 1.1 to 1.4 done; 179 + 27 tests green; review fixes in, accepted demo limits in implementation-decisions |
| 2 | Operations | Done | 2026-11-02 | Slices 2.1–2.4 done; 17+44 Operations, 29 platform, 185 contracts green (re-run 2026-11-02); ADRs ops 0001 and contracts 0016 confirmed by Majid; retro done; merged into master |
| 3 | Lending pure logic (TDD) | Done | 2026-11-02 | Slices 3.1–3.5; 1,498 tests green (re-run 2026-11-02); ADRs lending 0001–0003 reviewed by Majid (0003 point 5 changed to prepayment, point 9 nets prepaid interest); retro skipped; merged into master |
| 4 | Customers and Accounts | Ready for review | 2026-10-31 | Slices 4.1–4.5; customers 37, accounts 115, platform 33, contracts 221 green; accounts ADRs 0001–0003 and contracts 0017 await review; retro pending |
| 5 | Lending service | Not started | | |
| 6 | Cards and Ledger | Not started | | |
| 7 | Gateway and data import | Not started | | |
| 8 | End-to-end and AWS | Not started | | |

## Open questions

- 2026-10-31: Majid delegated D1 decisions for stages 2–8 to Claude (no questions). Each stage plan lists them under "Decisions to review"; their ADRs say "to be reviewed by Majid". Stage branches are chained (each from the previous), none merged into `master`.

- Account number format decided on 2026-10-30 (9 digits: 8 random plus Luhn). Its ADR is still to be written by Majid (workspace `docs/adr/0003`).

- 2026-11-02: stage 3 changed `Allocation.Apply` (new `LoanTerms` parameter, prepayment of later items) and the early payoff accrual. The chained branches `stage-4-customers-accounts` … `stage-8-e2e-aws` were built on the old stage 3; the stage 5 payment handler still calls the old signature and says "never prepaid". Bring them up to date before reviewing stage 4 and later. Done for `stage-4-customers-accounts` on 2026-11-02 (master merged in); stage 5 and later follow when their review starts.
