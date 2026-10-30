# Event-Driven Banking System: Phase 1 Design Plan

Date: 2026-10-29 · Author: Majid

## 1. Goal, scope and phases

Phase 1 delivers a runnable, event-driven banking system with unit and integration tests; the AI agent is built on top of it in phase 2. The project is for learning, portfolio and agent demos, not a commercial product.

| Phase | Content | Done when |
| --- | --- | --- |
| Phase 1 | Six services, contracts, messaging, initial data import, gateway, local and AWS deployment | All main flows pass integration tests; the system runs on AWS with imported data |
| Phase 2 | Agent on Microsoft Agent Framework and the Claude API, tools exposed as an MCP server | Out of scope for this document; only the baseline decisions are recorded |

Fixed project principles:

- **Documented decisions**: every decision is recorded with its rationale and the rejected alternatives (this document plus an ADR file in each service folder).
- **No hard-coding**: rates, thresholds and grace periods are product parameters or configuration.
- **Replaceable dependencies**: Kafka, file storage, exchange rates, email delivery and the language model all sit behind interfaces.
- **Near-zero cost**: wherever there is a choice, the free or cheaper option wins, as long as the principles above hold.

## 2. Business decisions

The bank is assumed to operate in Sweden with SEK as the base currency, but currency is present in all data and messages from day one. The bank has no branch concept. Items marked "(confirmed)" were proposed during design and confirmed by Majid on 2026-10-29.

### Customers and currency

| Topic | Decision | Rationale |
| --- | --- | --- |
| Customer type | A single "customer" concept; no individual/corporate split, no joint accounts, no joint loans | Simplicity; can be added later with separate tables if needed |
| Registration | Through the API (called by the agent in phase 2), plus a bulk import of initial customers | The only system user for now is the agent |
| KYC | None | Outside the learning goal |
| Branches | None; customers, accounts, loans, employees and journal entries are not tied to a branch | Simplicity; can be added later as a dimension if needed |
| Account number format | 9 digits without meaning: 8 random digits (the first 1–9) plus a Luhn check digit, generated behind `IAccountNumberGenerator`; uniqueness by a unique index and a new draw on collision (decided by Majid on 2026-10-30) | Reveals nothing (not the number or order of accounts); Luhn is the Swedish standard and catches every single-digit typo and almost every swap of two neighbouring digits; the generator interface keeps the format out of business logic. Rejected: a serial number (reveals count and order), 7 digits, Damm and mod 11 check digits |
| Currency | Every amount is always stored and transmitted with its currency (`Money`); each account has one currency; base currency is SEK | Multi-currency from day one, no later rewrite |
| Exchange rates | Fetched from an external service at conversion time; rate, source and timestamp are stored on the transaction. Default source: the free Riksbank API (confirmed) | Auditable; free and official source |

### Accounts, transfers and standing orders

| Topic | Decision | Rationale |
| --- | --- | --- |
| Account types | Current and savings; no deposit interest in phase 1 | As required |
| Overdraft | Not allowed | As required |
| Transfers | Only between accounts within the bank; transfers between different currencies allowed with on-the-spot conversion | As required |
| Fees | Zero, but the event contracts have room for a fee | Future change without breaking contracts |
| Standing orders | Can be created without sufficient balance; full balance required at execution; if insufficient, that run is recorded as failed and the customer is notified, with no retry (confirmed) | As required; retries add complexity without clear value |
| Statement and balance at a date | Derived from the transaction table (with balance after each entry) | No duplicate tables |

### Loans

There are two repayment methods, and loan kinds (car, mortgage, purchase financing) are simply products with different parameters. All parameters are defined on the product and copied onto the loan at disbursement, so a product change does not alter existing loans.

| Topic | Installment | Lump-sum / staged |
| --- | --- | --- |
| Repayment | Equal monthly installments (annuity) | Several stages; each stage repays an agreed share of principal plus interest accrued up to that date |
| Interest calculation | Annuity formula with monthly rate = annual rate ÷ 12 (30/360) | Actual/365 on outstanding principal, from the last payment to the stage date |
| Schedule | Generated at disbursement | Number, dates and principal share of each stage are set per loan at approval; the product only limits the maximum number of stages and term (confirmed) |
| Rate | Fixed | Fixed |

```
Monthly installment: A = P · r(1+r)^n / ((1+r)^n − 1),   r = R / 12
Stage interest:      I = P_outstanding · R · days / 365
```

| Rule | Decision |
| --- | --- |
| Loan currency | Same as the repayment account currency (confirmed) |
| Loan limit | None |
| Late penalty | Daily percentage on the overdue amount, after a 5-day grace period, from day six onward (confirmed); rate and grace period on the product |
| Classification | Overdue from the day after the due date; doubtful (default) after 90 days; thresholds configurable on the product |
| Payment allocation | Penalty, then interest, then principal; oldest installment first |
| Automatic payment | Debited from the repayment account on the due date; if the balance is insufficient the installment becomes overdue and is retried every business day (confirmed) |
| Manual payment | With a test card, for a specific installment or the overdue amount |
| Early payoff | Outstanding principal + interest to date + minimum interest; future interest beyond the minimum is waived |

Early payoff minimum interest (confirmed): percentage and cap are defined on the product (default 1% of the loan principal). If the remaining future interest is below the minimum, no discount is given.

```
Min    = min(p · Principal, Cap)
Waived = max(0, FutureInterest − Min)
```

### Cards, documents and appointments

| Topic | Decision | Rationale |
| --- | --- | --- |
| Cards | Simulated subsystem: a few predefined test card numbers with fixed behavior (always approve, always decline, insufficient funds); only for manual installment payments | Test success and failure scenarios without a real payment gateway |
| Loan application review | The system only provides the statuses and a status-change API; the decision is made by the agent (or a human) in phase 2 | As required |
| Documents and interactions | A customer interaction store: uploaded files, email replies, notes; linked to the loan application and information request | Agent input in phase 2 |
| Appointments | Only during loan review, in person at the bank; an employee has one appointment at a time | As required; no general calendar subsystem |
| Notifications | Email behind an interface; templates in English, Swedish later | Local testing with a fake mailbox |

### Accounting and reporting

| Topic | Decision |
| --- | --- |
| General ledger | Double-entry, per currency; posted only from events |
| Revenue recognition | Cash basis: interest and penalties recognized when collected |
| Currency conversion | Through a currency position account per currency |
| Output | Journal entries, trial balance |
| Customer reports | Account statement, balance at a date, loan status |
| Bank reports | Loan portfolio, arrears by classification, trial balance; no PDF in phase 1 |

## 3. Overall architecture

Six independent services sit behind one gateway and communicate only through Kafka events; every external dependency is behind an interface.

```
                 ┌─────────────────────────────┐
                 │  Agent + MCP server (phase 2)│
                 └──────────────┬──────────────┘
                                ▼
                 ┌─────────────────────────────┐
                 │  API Gateway (YARP) · REST   │
                 └──────────────┬──────────────┘
                                ▼
┌───────────────────────────────────────────────────────────────┐
│  Six services · .NET 10 · one folder each                      │
│                                                               │
│  Customers         Accounts           Cards                   │
│  customers,        accounts,          test cards,             │
│  documents,        transfers, FX,     manual installment      │
│  info requests,    standing orders,   payment                 │
│  appointments      statements                                 │
│                                                               │
│  Lending           Ledger             Operations              │
│  products,         double-entry       currencies,             │
│  applications,     journal, trial     business date,          │
│  loans, schedules, balance, reports   end of day              │
│  payoff                                                       │
└───────────────────────────────┬───────────────────────────────┘
                                ▲▼  events only
┌───────────────────────────────────────────────────────────────┐
│  Kafka, behind Bank.Messaging (outbox, inbox, dead-letter)     │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  Behind interfaces, swappable per environment                  │
│  PostgreSQL         File storage      Email          FX rates │
│  schema per service local disk · S3   Mailpit · SES  Riksbank │
└───────────────────────────────────────────────────────────────┘
```

Requests reach one service through the gateway; that service stores the change in its own schema and publishes the event through its outbox, and the other services react. In phase 2 the agent accesses the system only through the gateway.

## 4. Technical decisions (ADR)

Each row is an ADR and is also recorded as a separate file (`docs/adr/NNNN-title.md`) in the relevant service folder.

| # | Decision | Rationale | Rejected alternatives |
| --- | --- | --- | --- |
| 01 | .NET 10, ASP.NET Core Minimal API, EF Core with the Npgsql provider (briefly Dapper on 2026-10-30, workspace ADR 0001; back to EF Core the same day, workspace ADR 0002) | LTS release; the team's core expertise; unit of work, migrations and optimistic concurrency built in; raw SQL (`FromSql`, `ExecuteSqlAsync`) where LINQ has no equivalent, such as `FOR UPDATE SKIP LOCKED` | Dapper (SQL fully visible, but the project would maintain its own unit of work, connection factory and migration runner) |
| 02 | Vertical slice style; DDD only at the strategic level (service boundaries); calculation logic in pure functions under `Logic/` | Simplicity; financial logic without dependencies, the main TDD target | Domain/Application/Infrastructure layering, aggregates, repositories |
| 03 | Six services: Customers, Accounts, Cards, Lending, Ledger, Operations | Each has one responsibility and owns its data | Merging Cards into Accounts (different behavior and future) |
| 04 | Services communicate only through events; no synchronous service-to-service calls | Independent deployment and failure | Internal REST calls |
| 05 | Kafka behind our own abstraction in the `Bank.Messaging` package (`IEventPublisher`, `IEventHandler<T>`); implemented with Confluent.Kafka | Swapping to RabbitMQ or Azure Service Bus needs only a new adapter | Wolverine and MassTransit (code coupled to the framework; MassTransit v9 went commercial) |
| 06 | One topic per publishing service; message key = entity id (LoanId, AccountId) | Message order per entity is preserved | One topic per event type (too many topics) |
| 07 | Transactional Outbox for publishing and Inbox for receiving, in each service's schema | At-least-once delivery without losing messages | Publishing directly after commit |
| 08 | Idempotency: the message id is unique in the Inbox; duplicate messages are ignored; financial operations also have a unique business key (e.g. LoanId + installment number) | Duplicate messages in Kafka are certain | Relying only on Kafka exactly-once |
| 09 | Errors: three retries with increasing backoff, then a dead-letter topic | A poison message does not block the queue | Infinite retries |
| 10 | Contracts: C# records (source of truth) in the `Bank.Contracts` NuGet package on GitHub Packages; JSON Schemas with Iglu-style URIs generated from them and committed as snapshots; only optional fields may be added, any other change is a new MODEL; CI checks snapshots and version compatibility (changed 2026-10-30, bank-contracts ADR 0003) | Type safety, one copy of each contract, a language-neutral schema, no server and no cost, can move to a registry later | Hand-written schemas as source of truth (every event written twice), Iglu Server (limited license, extra server), Confluent Schema Registry (tied to Kafka) |
| 11 | One PostgreSQL instance; one schema and one database user per service, with access only to its own schema | Cost of a single server; the data boundary is enforced by permissions, not convention | Separate database per service |
| 12 | API Gateway with YARP; REST + OpenAPI | Free, .NET, identical locally and on AWS | AWS API Gateway (cost, environment differences) |
| 13 | Simulated authentication: the gateway sets the `X-Customer-Id` header; Keycloak or JWT later | Phase 1 focuses on banking logic | — |
| 14 | The bank business date lives in Operations; business logic never reads the system clock (`IBusinessClock`) | Time simulation and deterministic tests | `DateTime.Now` |
| 15 | End of day: Operations publishes `DayClosed`; each service runs its end-of-day work and replies with `DayCompleted`; once all have replied, the next day opens | Coordination without a saga | A separate scheduled job in each service |
| 16 | Money: `decimal` with currency; rounding to the currency's minor unit with AwayFromZero; rounding difference goes into the last installment | Deterministic and testable | `double` |
| 17 | One repository with a folder per service, plus `bank-contracts` (contracts and messaging packages), `bank-gateway`, `bank-platform` (Aspire AppHost, Docker Compose, AWS infrastructure) and `bank-data-import`; each service has its own CI and is deployed separately | Simple for one developer; services stay independent | A repo per service |
| 18 | File storage behind `IFileStorage`: local file system in development, S3 on AWS | No emulator needed (LocalStack and MinIO have both tightened their licenses) | LocalStack, MinIO |
| 19 | Email behind `IEmailSender`: Mailpit locally; on AWS Mailpit for now, or Amazon SES | Free; emails can be inspected in tests | Real SMTP |
| 20 | Exchange rates behind `IExchangeRateProvider`: Riksbank API; fixed rates in tests | Free and official | Commercial API |
| 21 | Audit: `PerformedBy` field and an audit table in each service | Traces the agent's actions in phase 2 | Central audit service |
| 22 | OpenTelemetry; Aspire dashboard locally and its standalone version on AWS | Free, one tool in both environments | CloudWatch (cost) |
| 23 | Testing: xUnit, Shouldly, Testcontainers (PostgreSQL, Kafka); CI with GitHub Actions; images on GHCR | Free; FluentAssertions went commercial | — |
| 24 | Phase 2: Microsoft Agent Framework on `IChatClient`; Claude by default, OpenAI swappable; tools in an MCP server in front of the gateway; English by default, Swedish and Persian supported | Model swap through configuration | — |

## 5. High-level data model

Each service owns only its own tables; reference data from other services (such as currencies) is kept in a local copy table updated from events. All tables have `CreatedAt`, `PerformedBy` and `Version` (optimistic concurrency); each schema also has `outbox_message` and `inbox_message` tables.

| Service | Table | Key fields |
| --- | --- | --- |
| Operations | Currency | Code, MinorUnits, IsActive |
| Operations | BusinessDay | Date, Status (Open, Closing, Closed), CompletedServices |
| Customers | Customer | CustomerId, Name, NationalId, DateOfBirth, Email, Phone, Address, PreferredLanguage, Status |
| Customers | Interaction | InteractionId, CustomerId, ApplicationId?, Kind (Upload, EmailReply, Note), FileKey?, Content, ReceivedOn |
| Customers | InformationRequest | RequestId, CustomerId, ApplicationId, RequestedItems, Status, DueDate |
| Customers | Employee | EmployeeId, Name |
| Customers | Appointment | AppointmentId, ApplicationId, CustomerId, EmployeeId, StartsAt, Status |
| Customers | Notification | NotificationId, CustomerId, Channel, Template, Status, SentAt |
| Accounts | Account | AccountId, Number, CustomerId, Type, Currency, Balance, Status, OpenedOn |
| Accounts | AccountEntry | EntryId, AccountId, BusinessDate, Amount, BalanceAfter, Kind, Reference, Description |
| Accounts | Transfer | TransferId, From, To, Amount, ConvertedAmount, FxRate, FxSource, FxAt, Status |
| Accounts | StandingOrder | Id, From, To, Amount, Frequency, StartDate, EndDate?, NextRunDate, Status |
| Accounts | StandingOrderRun | Id, StandingOrderId, RunDate, Status, Reason |
| Cards | TestCard | CardNumber, Behavior (Approve, Decline, InsufficientFunds), Currency |
| Cards | CardPayment | PaymentId, CardNumber, Amount, Purpose, LoanId?, Status |
| Lending | LoanProduct | ProductId, Name, RepaymentType, Currency, Min/MaxAmount, Min/MaxTerm, AnnualRate, PenaltyDailyRate, GraceDays, OverdueAfterDays, DefaultAfterDays, EarlyPayoffMinPercent, EarlyPayoffMinCap, MaxStages |
| Lending | LoanApplication | ApplicationId, CustomerId, ProductId, RequestedAmount, Term, ProposedStages?, Purpose, Status, StatusHistory |
| Lending | Loan | LoanId, ApplicationId, CustomerId, RepaymentAccountId, Principal, Currency, copy of product parameters, DisbursedOn, Status, Classification |
| Lending | ScheduleItem | LoanId, Number, DueDate, PrincipalDue, InterestDue, PenaltyDue, PaidPrincipal, PaidInterest, PaidPenalty, Status |
| Lending | LoanPayment | PaymentId, LoanId, Source (Account, Card, EarlyPayoff), Amount, Allocation, BusinessDate |
| Ledger | GlAccount | Code, Name, Type, Currency |
| Ledger | JournalEntry | EntryId, BusinessDate, Currency, SourceEvent, SourceMessageId |
| Ledger | JournalLine | EntryId, GlCode, Debit, Credit |

Loan application statuses: Submitted, UnderReview, InfoRequested, AppointmentScheduled, Approved, Rejected, Disbursed, Cancelled. A loan's status and classification are two separate fields: Status (Active, PaidOff, PaidOffEarly, WrittenOff) and Classification (Current, Overdue, Default).

## 6. Main events

About 25 events cover all of phase 1; Ledger listens to every financial event and posts a journal entry for each. Field-level details are in `docs/stages/stage-N.md`.

| Event | Publisher | Consumers | Effect |
| --- | --- | --- | --- |
| CurrencyActivated | Operations | All | Update local reference data copy |
| BusinessDayOpened | Operations | All | Update the local business date |
| DayClosed | Operations | Accounts, Lending | Run standing orders, installment due dates, penalties and classification |
| DayCompleted | Accounts, Lending | Operations | Once all are received, the next day opens |
| CustomerRegistered | Customers | Accounts, Lending | Store a local customer copy |
| AccountOpened | Accounts | Lending | Account becomes selectable as a repayment account |
| MoneyDeposited, MoneyWithdrawn | Accounts | Ledger | Cash deposit/withdrawal entry |
| MoneyTransferred | Accounts | Ledger | Currency conversion entry, only when the two currencies differ |
| StandingOrderFailed | Accounts | Customers | Email the customer |
| LoanApplicationSubmitted, LoanApplicationStatusChanged | Lending | Customers | Record in interaction history |
| InformationRequested | Lending | Customers | Create an information request and email |
| InformationReceived | Customers | Lending | Return the application to UnderReview |
| AppointmentScheduled | Customers | Lending | Change the application status |
| LoanDisbursed | Lending | Accounts, Ledger | Credit the customer account; loan entry |
| InstallmentDue | Lending | Accounts | Attempt debit from the repayment account |
| PaymentCollected | Accounts | Lending | Allocate the installment or settle the payoff |
| PaymentFailed | Accounts | Lending, Customers | Installment overdue; email |
| CardPaymentCaptured | Cards | Lending | Allocate the payment |
| LoanPaymentAllocated | Lending | Ledger | Entry with the principal, interest and penalty split |
| CardPaymentDeclined | Cards | Customers | Email |
| PenaltyAccrued | Lending | — | Reporting only; no entry (cash-basis revenue) |
| LoanClassificationChanged | Lending | Customers | Warning email |
| LoanPayoffRequested | Lending | Accounts | Debit the payoff amount, reply PaymentCollected or PaymentFailed |
| LoanPaidOffEarly | Lending | Customers | Confirmation email |

Sample journal entries:

| Event | Debit | Credit |
| --- | --- | --- |
| LoanDisbursed | LoansReceivable | CustomerDeposits |
| LoanPaymentAllocated (account or payoff) | CustomerDeposits | LoansReceivable (principal), InterestIncome, PenaltyIncome |
| LoanPaymentAllocated (card) | CardClearing | LoansReceivable, InterestIncome, PenaltyIncome |
| MoneyTransferred (two currencies) | CustomerDeposits (source currency) | FxPosition (source currency) |
| ″ | FxPosition (target currency) | CustomerDeposits (target currency) |

## 7. Initial data

About 100 customers with 1 to 2 years of history are imported from the [databasesample.com](https://databasesample.com/datasets/bank-sample-database) dataset (CC0 license), without a simulator.

1. **Selection**: the 100K version of the dataset; 100 varied customers are chosen (with and without loans, with arrears, with standing orders).
2. **Transformation**: dates are shifted so the latest record lands on the system start date; amounts are converted to SEK with a fixed factor; the dataset's branch data is ignored.
3. **Import through the API, not direct SQL**: the `bank-data-import` tool sends commands through the gateway so that all events, journal entries and validation rules run exactly as in real operations.
4. **Loans**: the dataset has no installment schedule, so each loan is disbursed in our system with the same parameters, the schedule is built by our own logic, and the dataset's real payments are applied to it in chronological order.
5. **Time**: the bank business date advances day by day during the import so that end of day, penalties and classifications are calculated realistically.
6. **Verification**: after the import, each account balance is compared with the sum of its transactions and the trial balance with zero.

Risk: the dataset's payment behavior may not match our installment schedule (for example, different payment amounts). In that case the payment is mapped to the nearest installment and the discrepancy is logged in the import report.

## 8. Testing strategy

Financial logic is written test-first, and each service has three test levels; each service's CI runs all of them on every push.

| Level | What | Tools | Examples |
| --- | --- | --- | --- |
| Unit | Pure functions in `Logic/` | xUnit, Shouldly | Annuity schedule, Actual/365 interest, payment allocation, early payoff minimum interest, penalty, classification |
| Service integration | Endpoints and handlers with a real database and Kafka | Testcontainers | A duplicate message has an effect only once; the outbox publishes nothing after a rollback |
| Contract | Each record matches its schema snapshot; new version compatible with the previous one | Tests in `bank-contracts` | An incompatible change without a MODEL bump breaks the build |
| End-to-end | A few scenarios across all services with Aspire | xUnit + Aspire testing | Loan application through early payoff; failed installment through default; cross-currency transfer |

Edge cases that must be tested: leap years in Actual/365, last-installment rounding, overpayment, two concurrent messages on one account, end of day in the middle of a transfer.

## 9. Deployment

One definition, two environments: local with .NET Aspire, and AWS on one small EC2 instance that runs only during demos. The two environments differ only in configuration and adapters.

| Component | Local | AWS |
| --- | --- | --- |
| Runtime | Aspire AppHost from the `bank-platform` folder | Docker Compose on one EC2 instance (about 4 GB RAM, ARM) |
| Kafka | Container (KRaft mode, no ZooKeeper) | Same container on EC2 |
| PostgreSQL | Container | Same container, data on EBS, nightly backup to S3 |
| Files | Local file system | S3 |
| Email | Mailpit | Mailpit (SES later) |
| Observability | Aspire dashboard | Standalone Aspire dashboard |
| Images | Local build | GitHub Actions, published to GHCR |
| Infrastructure | — | AWS CDK in C# |

Approximate cost: a few dollars a month if the EC2 instance runs only a few hours a week; about 25 to 30 dollars if it runs continuously. These figures have not been checked against published AWS prices and must be verified before deployment. A later move to MSK and RDS is only a connection string change.

## 10. Phase 1 roadmap

Phase 1 is built in eight sequential stages; each stage closes with green tests and an updated ADR, and the next stage does not start until Majid approves. Each stage has a self-contained specification in `docs/stages/stage-N.md`; cross-cutting implementation choices are in `docs/implementation-decisions.md`. The project is also a learning project: each stage runs as small slices with a checkpoint after each, Majid makes the design decisions and writes selected parts himself, and Claude explains and reviews (see "Learning mode" in `docs/implementation-decisions.md` and the "Learning" section of each stage file).

1. **Foundation**: `bank-contracts` (first schemas + compatibility CI), `Bank.Messaging` (Kafka adapter, outbox, inbox, dead-letter), `bank-platform` with Aspire, PostgreSQL and Kafka.
2. **Operations**: currencies, business date, the `DayClosed` / `DayCompleted` cycle.
3. **Lending, pure logic (TDD)**: annuity, Actual/365 and stages, payment allocation, penalty, classification, early payoff minimum interest.
4. **Customers and Accounts**: customer registration, accounts, deposits and withdrawals, transfers with currency conversion, standing orders, statements and balance at a date.
5. **Lending, service**: products, applications and statuses, disbursement, due dates, automatic payment, early payoff; documents, information requests and appointments in Customers.
6. **Cards and Ledger**: test cards and manual installment payment; journal entries, trial balance and reports.
7. **Gateway and import**: YARP, simulated authentication, `bank-data-import` with 100 customers.
8. **End-to-end and AWS**: end-to-end scenarios, CDK, deployment on EC2.

## 11. Confirmed assumptions and out of scope

All assumptions below were confirmed on 2026-10-29.

- [x] Exchange rate source: the free Riksbank API
- [x] Staged loans: stages are set per loan at approval; Actual/365 interest on outstanding principal from the last payment
- [x] Early payoff minimum interest: the lower of (percentage × loan principal, cap); if future interest is lower, no discount
- [x] AWS deployment on one EC2 instance with Docker Compose
- [x] Loan currency equals the repayment account currency
- [x] Penalty is calculated from day six (after the 5-day grace period), not from day one
- [x] A failed installment is retried from the account every business day; a failed standing order is not retried

Out of scope for phase 1: the agent, branches, corporate customers, joint accounts and loans, guarantors and collateral, KYC, deposit interest, interbank transfers, real bank cards, variable rates, daily interest accrual, real authentication, PDF output, a full loan review workflow.
