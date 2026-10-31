# Phase 1 Progress

Updated by `/close-stage`. One row per stage from section 10 of `phase1-design-plan.md`. Statuses: Not started, In progress, Ready for review, Done. The project runs in learning mode (`docs/implementation-decisions.md`); what was learned per stage is in `docs/learning-log.md`.

| Stage | Name | Status | Closed on | Notes |
| --- | --- | --- | --- | --- |
| 0 | Design and requirements | Done | 2026-10-29 | All assumptions in section 11 confirmed |
| 1 | Foundation: contracts, messaging, platform | Done | 2026-10-30 | Slices 1.1 to 1.4 done; 179 + 27 tests green; review fixes in, accepted demo limits in implementation-decisions |
| 2 | Operations | Ready for review | 2026-10-31 | Slices 2.1–2.4 done; 17+44 Operations, 29 platform, 185 contracts green; ADRs ops 0001 and contracts 0016 await Majid's sign-off; retro pending |
| 3 | Lending pure logic (TDD) | Ready for review | 2026-10-31 | Slices 3.1–3.5; 1,491 tests green; lending ADRs 0001–0003 await Majid's review; retro pending |
| 4 | Customers and Accounts | Ready for review | 2026-10-31 | Slices 4.1–4.5; customers 37, accounts 115, platform 33, contracts 221 green; accounts ADRs 0001–0003 and contracts 0017 await review; retro pending |
| 5 | Lending service | Ready for review | 2026-10-31 | Slices 5.1–5.6; lending 1,605, customers 58, accounts 120, operations 61, platform 35 green; lending ADRs 0004–0005 await review; retro pending |
| 6 | Cards and Ledger | Ready for review | 2026-10-31 | Slices 6.1–6.4; cards 13, ledger 28, lending 1,611, customers 58, platform 39 green; ledger ADRs 0001–0002 and lending 0006 await review; retro pending |
| 7 | Gateway and data import | Ready for review | 2026-10-31 | Slices 7.1–7.5; gateway 9, data-import 33+1, platform 47 green; Bank.Messaging.Kafka 0.6.1 fix (all services moved to 0.6.1); data-import ADR 0001 awaits review; real export's column names unchecked; retro pending |
| 8 | End-to-end and AWS | Not started | | |

## Open questions

- 2026-10-31: Majid delegated D1 decisions for stages 2–8 to Claude (no questions). Each stage plan lists them under "Decisions to review"; their ADRs say "to be reviewed by Majid". Stage branches are chained (each from the previous), none merged into `master`.

- Account number format decided on 2026-10-30 (9 digits: 8 random plus Luhn). Its ADR is still to be written by Majid (workspace `docs/adr/0003`).
