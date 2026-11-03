# Slice 1: Simulate a loan

This document has three parts. Part 1 is the big picture: a customer or the agent asks "what would this loan cost?" and nothing may change. Part 2 covers the building blocks: one pure function, one endpoint and the product checks it shares with submission. Part 3 explains why it is built this way. Tests, limits and questions come after.

---

## Part 1. The big picture

### 1.1 The goal

Before applying for a loan, people want to know what it costs: the monthly installment, the interest over the whole term and the total they pay back. In phase 2 the agent will answer exactly this question. So Lending needs a read-only endpoint. It takes a product, an amount and a term, and returns the installment, the total interest, the total cost and the full schedule.

### 1.2 Why it is hard

- **The numbers must match a real loan.** If the simulation used its own formula, or a rate typed into the code, a customer could be quoted one installment and then get another one at disbursement. The simulation has to use the same `Amortization` code and the same product parameters as disbursement.
- **The same limits apply.** Asking about a CAR loan of 10,000 SEK (below the product's minimum) or about 84+ months must fail the same way submission fails.
- **Nothing may happen.** A simulation stores no row and publishes no event, so it can be called any number of times, by anyone.

---

## Part 2. The building blocks and how they connect

```mermaid
sequenceDiagram
    participant C as Caller (customer, staff, agent)
    participant G as Gateway
    participant E as LoanSimulation endpoint
    participant DB as lending schema (read only)
    participant L as Logic: Amortization, LoanCost
    C->>G: POST /api/lending/loan-products/CAR/simulation {amount, termMonths, stages?}
    G->>E: POST /loan-products/CAR/simulation (prefix removed)
    E->>DB: loan_product CAR, currency_ref SEK (minor units)
    E->>E: LimitProblem (amount, currency, term); stage plan present only for staged
    E->>L: Terms(product, amount, term, today) → BuildAnnuity / BuildStaged
    L-->>E: schedule
    E->>L: LoanCost.Summarize(schedule)
    E-->>C: 200 {installment, totalInterest, totalCost, schedule}
```

### 2.1 The gateway route (no change)

`core_banking/bank-gateway/src/Bank.Gateway/Routes.cs` maps `/api/{service}/{**rest}` to the service for every HTTP method, and removes the `/api/{service}` prefix. So `/api/lending/loan-products/CAR/simulation` reaches the new endpoint without any gateway change. The gateway still asks for `X-Api-Key` and for one of `X-Customer-Id` or `X-Performed-By`.

### 2.2 `LoanCost.Summarize` (pure logic)

`core_banking/bank-lending/src/Bank.Lending.Api/Logic/LoanCost.cs`. Given a schedule, it returns:

- **Installment**: the first item's principal + interest, which is the annuity amount A (only the last item can differ by a few öre of rounding). For a staged loan the stages differ, so it is `null` and the schedule shows each stage.
- **TotalInterest**: the sum of every item's interest.
- **TotalCost**: principal + total interest. A new schedule has no penalties.

It reads the schedule only. So the totals can never disagree with the items shown next to them.

### 2.3 The endpoint `POST /loan-products/{code}/simulation`

`core_banking/bank-lending/src/Bank.Lending.Api/Features/Applications/LoanSimulation.cs`. In order:

1. It loads the product by code. An unknown product returns 404 `product_not_found`.
2. It checks the amount, currency and term with `ApplicationEndpoints.LimitProblem`, which returns 422 `amount_out_of_range` / `term_out_of_range`.
3. It checks that a staged product has a stage plan and an annuity product has none (422 `stage_plan_required` / `stages_not_allowed`).
4. It builds `LoanTerms` with `ApplicationEndpoints.Terms`, the same function disbursement uses, with today's business date (`IBusinessClock`) as the assumed disbursement date.
5. It calls `Amortization.BuildAnnuity` or `BuildStaged`. Errors in a stage plan come back as 422 with the logic's own code (for example `too_many_stages`).
6. It returns `SimulationResponse`, which holds the product, amount, term, annual rate, assumed disbursement date, the three totals and the schedule (number, due date, principal, interest, total).

It is mapped in `Program.cs` next to the other Lending endpoints. Its OpenAPI metadata (`WithName`, `WithSummary`, `Produces`, `ProducesProblem`) appears in the generated `/openapi/v1.json`.

### 2.4 The shared product checks

`core_banking/bank-lending/src/Bank.Lending.Api/Features/Applications/ApplicationEndpoints.cs`. The amount and term checks were inline in `SubmitAsync`. They moved into `LimitProblem(product, amount, termMonths)`, which submission and simulation both call. `StageProblem` now takes the amount, term and stages instead of the whole submit request. `ToPlan(stages)` turns request stages into a `StagePlan`, and disbursement uses it too. Behaviour did not change: the existing application and disbursement tests still pass.

---

## Part 3. Why it is designed this way

### 3.1 Decisions

This slice has no D1 decision. The four D1 decisions of this work (paging, GET or POST for searches, matching and indexes, how "staff only" is enforced) belong to slices 2–4.

Claude's choices (D2):

- **POST, not GET.** A staged product needs a list of stages, which does not fit a query string well. The body holds no personal data. The response is 200, not 201, because nothing is created.
- **Path `/loan-products/{code}/simulation`.** The product is the resource being asked about, and products are already found by code (`GET /loan-products`).
- **Disbursement date = today's business date.** A real loan counts from the day it is disbursed. The simulation says which date it assumed (`DisbursedOn`), so the due dates make sense.
- **Installment is null for staged loans.** The alternatives were "first stage" or "largest stage". Both would mislead, because the stages differ by design.
- **Simulation sits in `Features/Applications`.** It shares the product checks with submission, which live there.
- **Customers and staff may both call it.** No ownership check is needed, because nothing in it belongs to a customer.

### 3.2 Rejected alternatives

- **A separate simulation formula** (for example only the annuity formula, without building a schedule). It would be quicker, but it would differ from disbursement in rounding and in the last item.
- **A new `Logic/` function that also validates the product.** Product limits are a feature concern (they read a database row). `Logic/` stays pure and takes `LoanTerms`.

### 3.3 Patterns

- **Vertical slice**: one file holds the endpoint, its request and its response.
- **Functional core, imperative shell**: `Amortization` and `LoanCost` are pure; the endpoint does the I/O around them.
- **Single source of truth for rules**: one `LimitProblem` and one `Terms` serve submission, simulation and disbursement.

---

## What was tested

- `tests/Bank.Lending.UnitTests/LoanCostTests.cs` (written first and seen failing): CAR (A = 6,170.85, interest 22,150.54), MORTGAGE (interest 1,541,966.48), and a three-stage PURCHASE (interest 4,436.30, no installment). The arithmetic is in the comments.
- `tests/Bank.Lending.IntegrationTests/SimulationTests.cs` (Testcontainers PostgreSQL): the CAR schedule, a customer caller with nothing stored or published (no application, no loan, no new outbox row), a staged simulation, four out-of-product cases, an invalid stage plan and an unknown product.

```text
$ cd core_banking/bank-lending && dotnet test
Passed!  - Failed: 0, Passed: 1577, Skipped: 0, Total: 1577 - Bank.Lending.UnitTests.dll
Passed!  - Failed: 0, Passed:   55, Skipped: 0, Total:   55 - Bank.Lending.IntegrationTests.dll
```

## What to watch later

- The quote holds only for disbursement today. A loan disbursed on a later day gets different due dates, and for staged loans different interest.
- Products have no fees today, so total cost = principal + interest. If fees are added, `LoanCost` must include them.

## Check your understanding

1. Why does the simulation call `ApplicationEndpoints.Terms` instead of building `LoanTerms` itself?
2. What would a customer see if the simulation rounded each installment differently from disbursement?
3. Why is this endpoint safe to call repeatedly without an `Idempotency-Key`, while `POST /transfers` needs one?
