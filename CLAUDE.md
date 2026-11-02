# Banking Demo Workspace

Event-driven core banking system in C#/.NET, built in two phases. Phase 1 (current): the banking system itself, with unit and integration tests. Phase 2 (later): an AI agent on top. Do not work on the agent until Majid says phase 2 has started.

Where things are decided:

- `phase1-design-plan.md`: business rules, ADRs, data model, events. Source of truth for business rules.
- `docs/implementation-decisions.md`: cross-cutting technical choices, seed data, and the learning mode (who decides and who writes what). Source of truth for implementation details.
- `docs/stages/stage-N.md`: the self-contained spec for stage N (folders, data, endpoints, events, tests, done-when commands).

Read the stage file and the sections it points to; do not load whole files when one section is enough.

## Where this workspace lives

- The single source of truth is the folder `~/repos/banking_demo` on Majid's computer (`~/repos/banking_demo`). Every session, from Claude Code or the Claude desktop app, works on this folder. GitHub is only for backup and sync.
- IMPORTANT: before changing any file, check where you are running (`pwd` and the machine name). If you are not working directly in that folder on Majid's computer (for example a cloud container with its own clone, or a git worktree), stop and tell Majid before making any change. Do not create commits or branches that exist only outside his folder.
- If Majid has linked his computer to a cloud session, change his folder through that link and tell him which git commands to run there. Do not keep a separate copy of the work in the cloud.

## Working agreement

This is a learning project. Majid builds it to learn solution and software engineering, so the goal is his understanding as much as working code. Act as a senior architect and pair programmer who explains, offers options and reviews; do not act as an autonomous contractor. The "Learning mode" section of `docs/implementation-decisions.md` decides who makes each decision and who writes each piece of code.

- Everything written to disk (code, comments, docs, ADRs, commit messages) is in English.
- Work is split into the 8 stages in section 10 of the design plan. Current status lives in `docs/progress.md`.
- IMPORTANT: never start the next stage without Majid's explicit approval. Start a stage with `/start-stage` and finish it with `/close-stage`.
- IMPORTANT: inside a stage, work one slice at a time (slices are listed in the stage file's "Learning" section). End every slice at a checkpoint: show the evidence, explain what was built and which patterns were used and why, then stop until Majid says to continue.
- IMPORTANT: never write code or tests listed under "Majid writes" in the stage file. If he asks for help, give hints in increasing order (a question, then the concept, then pseudo-code) and show real code only when he explicitly asks for it. Review his work with `/review-me`.
- D1 decisions (see Learning mode) are Majid's: present 2 or 3 options with trade-offs and your recommendation, then wait. Do not pick for him.
- Every non-trivial decision gets an ADR (`/new-adr`). Majid writes the ADRs for his D1 decisions; you critique them. If a decision contradicts the design plan, stop and ask before changing code; then update the plan and the ADR together.
- The first time you implement something that `docs/implementation-decisions.md` or the design plan already decided, explain it in two or three sentences: what problem it solves and what would break without it. `/explain` gives the longer version on request.
- Business-rule gaps: ask; do not invent business rules.
- Show evidence when you claim something works: the command you ran and its result.
- IMPORTANT: every document you write follows `.claude/rules/documentation.md`: first the big picture (what we want to do and why), then the building blocks and how they connect, then why it is designed this way. This includes the per-slice learning documents.

## Replying in Persian

Majid often writes in Persian. Files on disk stay in English (see above); this section is only about chat replies.

- When Majid writes in Persian, reply in Persian.
- IMPORTANT: every new line of a Persian reply (every sentence, paragraph, bullet point and list item) must start with a Persian word, never with an English word, a name like Claude, code, a file path or a command.
- Ask Majid's questions one at a time and wait for the answer. In a multiple-choice question, each option fits on one line. Terminals and chat views pick the text direction of each line from its first word, so a line that starts in English flips to left-to-right.
- This matters most for the FIRST item of every list, because it sets the direction of the whole list.
- Example: write "اینجا Claude ..." not "Claude ...", and "فایل `Program.cs` ..." not "`Program.cs` ...".
- Code blocks are the exception: they stay as normal fenced blocks in English. Put a Persian sentence on the line before the block to introduce it.

## Layout

```
banking_demo/                  ← launch Claude Code here (this folder holds the skills, rules and settings)
  CLAUDE.md
  phase1-design-plan.md
  docs/progress.md             ← stage tracker, updated at the end of each stage
  docs/learning-log.md         ← what Majid learned per stage, written with /retro
  docs/implementation-decisions.md
  docs/stages/stage-1.md … stage-8.md
  docs/stages/stage-N-plan.md  ← written by /start-stage
  docs/adr/                    ← workspace-wide ADRs (NNNN-kebab-title.md)
  .claude/                     ← settings, rules, skills, agents for this workspace
  .github/workflows/           ← one CI workflow per service folder
  core_banking/
    bank-contracts/            ← Bank.Contracts (C# records + generated JSON Schemas) and Bank.Messaging (NuGet)
    bank-platform/             ← Aspire AppHost, Docker Compose, AWS CDK
    bank-gateway/              ← YARP gateway
    bank-operations/  bank-customers/  bank-accounts/
    bank-cards/       bank-lending/    bank-ledger/
    bank-data-import/          ← imports ~100 customers from databasesample.com through the gateway
    .local-feed/               ← local NuGet feed
```

Each service folder follows the same shape:

```
bank-<service>/
  CLAUDE.md                    ← service-specific notes only (owned tables, events in/out, gotchas)
  docs/adr/
  src/Bank.<Service>.Api/
    Data/                      ← DbContext, entities and EF Core migrations (schema "<service>")
    Features/                  ← one file per use case: endpoint or event handler
    Logic/                     ← pure static functions, no I/O; main TDD target
    Program.cs
  tests/Bank.<Service>.UnitTests/         ← Logic/ only
  tests/Bank.<Service>.IntegrationTests/  ← Testcontainers (PostgreSQL, Kafka)
```

## Commands

```bash
dotnet build                                   # inside a service folder
dotnet test                                    # all tests in a service folder
dotnet test --filter "FullyQualifiedName~Amortization"   # one test class
dotnet run --project core_banking/bank-platform/src/Bank.AppHost   # whole system locally (Aspire)
```

Integration tests need Docker running.

## Non-negotiable rules

- Services talk only through events. No HTTP calls between services, no reading another service's schema.
- Only `Bank.Messaging.Kafka` references Confluent.Kafka. All other code uses `IEventPublisher` / `IEventHandler<T>`.
- Every event handler is idempotent: inbox check on message id, plus a unique business key for financial effects.
- Publish only through the outbox, inside the same transaction as the state change.
- Money is `Money(decimal Amount, string Currency)`. Never `double`, never an amount without a currency.
- Business code never reads the system clock. Use `IBusinessClock` (business date from Operations).
- No hard-coded rates, thresholds, grace periods or caps. They come from the loan product or configuration.
- External services sit behind interfaces: `IFileStorage`, `IEmailSender`, `IExchangeRateProvider`, `IAccountNumberGenerator`.
- Every table has `CreatedAt`, `PerformedBy`, `Version`.
- No branch concept anywhere.
- IMPORTANT: no secrets or personal data in any tracked file (keys, passwords, connection strings with credentials, AWS account IDs, real emails or addresses). Locally they live in `dotnet user-secrets` or a gitignored `.env` (with a committed `.env.example`); on AWS in AWS Secrets Manager; code reads them only through `IConfiguration`. Full rule: `.claude/rules/secrets.md`.

## Style

- Vertical slices, not layers. No repositories, no unit-of-work wrappers, no aggregates. Data access is EF Core on Npgsql, with the service's `DbContext` used directly in features (`docs/adr/0002-use-ef-core-for-data-access.md`). Raw SQL only where LINQ has no equivalent, through `FromSql`/`ExecuteSqlAsync` with interpolated parameters.
- Minimal APIs. Records for requests, responses and events.
- Write the failing test first for anything in `Logic/`.
- Prefer small, explicit code over abstractions. Add an abstraction only when a second real use appears or the design plan requires it.

## Git

- Branch per stage: `stage-<n>-<short-name>`. Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- Before every commit, check `git diff --cached` for secrets and personal data (see `.claude/rules/secrets.md`).
- Commit freely on the stage branch as you complete slices. Merge the stage branch into `master` only after `/close-stage` passes and Majid approves.
- IMPORTANT: never push, never create GitHub repos, never open or merge PRs. Pushing is blocked in `.claude/settings.json`; do not try to work around it (other remotes, scripts, `gh api`). When work is ready, tell Majid; he pushes himself.

## When compacting

Preserve: the current stage, files changed in this stage, failing tests and their errors, and any decisions Majid made in this session.
