# bank-customers

Customers and what the bank tells them. Stage 4: registration and notifications. Employees, interactions, information requests and appointments come in stage 5. Schema `customers`, topic `bank.customers.events`. Plan: `docs/stages/stage-4-plan.md`.

## Tables

- `customer`: unique `national_id`, contact details, `preferred_language` (en default, sv, fa), `status`.
- `notification`: one row per message sent; unique `source_event_id`, so an event produces at most one notification.
- `business_day` (local copy, `AddBusinessDayConsumer`), `outbox_message`, `inbox_message`.

## Events

- Publishes `CustomerRegistered` (key = customer id, `OccurredOn` = business date).
- Consumes `StandingOrderFailed` (from accounts) → English email via `IEmailSender`; `BusinessDayOpened`/`DayClosed` (from operations) for `IBusinessClock`.

## Endpoints

`POST /customers` (400 validation problem, 409 `national_id_taken`), `GET /customers/{id}`, `PATCH /customers/{id}` (email, phone, address, language only). `X-Customer-Id` for another customer → 404.

`POST /customers/search` (`Features/Customers/SearchCustomers.cs`, branch feat-agent-read-apis): name contains (ILIKE, wildcards escaped), email equal ignoring case, phone equal on digits (`CustomersDbContext.RegexpReplace` with 'g'), customer number = customer id, national id equal after trimming (used by bank-identity's login, workspace ADR 0007); AND; returns `CustomerSummary`. Keyset paging on (name, id) via `Features/Paging.cs` (workspace ADR 0004), POST so personal data stays out of URLs (0005), staff only via `StaffAccess` (0006). No index (ADR 0001): sequential scan, revisit with pg_trgm when the table grows.

## Gotchas

- The email is sent before the handler's transaction commits: at least once, never zero. A crash in between can send it twice.
- `ConnectionStrings:mailpit` must be `Endpoint=smtp://host:port` (Aspire's format); `Email:From` is a placeholder address.
- Registration needs a business date: until `BusinessDayOpened` arrives, `IBusinessClock.Today` throws.
- Uploaded files go through `IFileStorage`, chosen by `Files:Provider`: `Local` (default, `Files:Root`) or `S3` (`Files:Bucket`, `Files:Prefix` = `files/`; credentials from the default chain, the instance role on AWS). An unknown provider, or S3 without a bucket, fails at startup. `S3FileStorageTests` run against LocalStack 4.9 (the last image without an account token; MinIO images are no longer pullable).
- `Regex.Replace` in LINQ is translated without the 'g' flag (first match only); use `CustomersDbContext.RegexpReplace`.
- Never log search filter values. `CustomerSearchTests.No_search_value_reaches_the_logs` captures every log level through `Support/CapturingLoggerProvider`.
- Tests: `Support/CustomersApp` extends `BankServiceApp<Program>` from `Bank.Testing` and replaces `IEmailSender` with `InMemoryEmailSender`.

## Commands

```bash
dotnet build && dotnet test                         # integration tests need Docker
dotnet tool restore && dotnet ef migrations add <Name> --project src/Bank.Customers.Api --output-dir Data/Migrations
```
