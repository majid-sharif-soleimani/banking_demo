using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bank.Contracts;
using Bank.DataImport.Logic;
using Bank.DataImport.Source;

namespace Bank.DataImport;

public sealed record ImportOptions(
    decimal AmountFactor = 10.0m, int Customers = 100, int Seed = 20260101, DateOnly? StartDate = null, bool Force = false,
    TimeSpan? DayTimeout = null, string Username = "admin", string Password = "admin");

public sealed class ImportReport
{
    public int Customers { get; set; }
    public int Accounts { get; set; }
    public int Days { get; set; }
    public Dictionary<string, int> Applied { get; } = [];
    public List<string> Selection { get; } = [];
    public List<string> Skipped { get; } = [];
    public List<string> Mismatches { get; } = [];
    public List<string> VerificationErrors { get; } = [];

    public bool Verified => VerificationErrors.Count == 0;

    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public void Count(string what) => Applied[what] = Applied.GetValueOrDefault(what) + 1;

    public string ToMarkdown()
    {
        var (from, to) = (From, To);
        var text = new StringBuilder();
        text.AppendLine("# Import report").AppendLine();
        text.AppendLine($"Replayed {Days} business days, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, through the gateway.").AppendLine();
        text.AppendLine("| What | Count |").AppendLine("| --- | --- |");
        text.AppendLine($"| Customers | {Customers} |").AppendLine($"| Accounts | {Accounts} |");
        foreach (var (what, count) in Applied.OrderBy(a => a.Key))
        {
            text.AppendLine($"| {what} | {count} |");
        }

        text.AppendLine().AppendLine("## Selection").AppendLine();
        Selection.ForEach(s => text.AppendLine($"- {s}"));

        text.AppendLine().AppendLine($"## Skipped rows ({Skipped.Count})").AppendLine();
        Skipped.ForEach(s => text.AppendLine($"- {s}"));
        text.AppendLine().AppendLine($"## Payment amount mismatches ({Mismatches.Count})").AppendLine();
        Mismatches.ForEach(s => text.AppendLine($"- {s}"));
        text.AppendLine().AppendLine($"## Verification: {(Verified ? "passed" : "FAILED")}").AppendLine();
        VerificationErrors.ForEach(s => text.AppendLine($"- {s}"));
        return text.ToString();
    }
}

/// <summary>
/// Replays the selected history day by day through the gateway (stage 7 file, steps 0 and 5–8), so every rule, event and
/// journal entry runs as in real operations. One request after another, one day after another (ADR 0001); after each day
/// it closes the business day and polls until the next one is open. Every money request carries an Idempotency-Key from
/// its source row, so a retry within a run repeats instead of duplicating. A second run is refused: customers cannot be
/// found again by their source id, so it could only replay empty days (stage 7 file: fresh database only).
/// </summary>
public sealed class Importer(HttpClient gateway, ImportOptions options, TextWriter log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Guid> customers = [];
    private readonly Dictionary<string, Guid> accounts = [];
    private readonly Dictionary<string, (Guid LoanId, Guid AccountId)> loans = [];
    private readonly Dictionary<string, int?> maxStages = [];

    public ImportReport Report { get; } = new();

    private sealed record LoginView(string Token);

    /// <summary>
    /// Logs in as admin (workspace ADR 0007), so the gateway records every change as made by "admin". The replay never
    /// idles for five minutes, but a login as admin elsewhere ends this session and stops the import (exit 4).
    /// </summary>
    private async Task LoginAsync(CancellationToken ct)
    {
        using var response = await gateway.PostAsJsonAsync("/api/identity/login", new { username = options.Username, password = options.Password }, Json, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login as {options.Username} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }

        var login = await response.Content.ReadFromJsonAsync<LoginView>(Json, ct);
        gateway.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login!.Token);
    }

    public async Task<ImportReport> RunAsync(SourceData source, CancellationToken ct)
    {
        // Every call after this, reads included, carries the session token: the gateway refuses others (401).
        await LoginAsync(ct);

        foreach (var code in new[] { "SEK", "EUR", "USD" })
        {
            await SendAsync(HttpMethod.Post, $"/api/operations/currencies/{code}/activate", null, null, ct);
        }

        // One anchor: the replay starts on the bank's current business date, and the history is shifted onto it.
        var current = await CurrentDayAsync(ct);
        var startDate = current.Date;
        if (current.Status != "Open" || (options.StartDate is { } expected && expected != current.Date && !options.Force))
        {
            throw new InvalidOperationException(
                $"The bank is at {current.Date:yyyy-MM-dd} ({current.Status}), not a fresh {options.StartDate:yyyy-MM-dd}. Import into a fresh database, or pass --force.");
        }

        foreach (var product in await gateway.GetFromJsonAsync<List<ProductView>>("/api/lending/loan-products", Json, ct) ?? [])
        {
            maxStages[product.Code] = product.MaxStages;
        }

        var data = Prepare(source, startDate);
        var lastDay = data.Days.Keys.DefaultIfEmpty(startDate).Max();
        log.WriteLine($"Importing {data.Customers.Count} customers over {lastDay.DayNumber - startDate.DayNumber + 1} days.");

        (Report.From, Report.To) = (startDate, lastDay);
        await RegisterAsync(data, ct);
        if (Report.Accounts == 0)
        {
            throw new InvalidOperationException("No account was opened, so there is nothing to replay; no business day was closed.");
        }

        for (var day = startDate; day <= lastDay; day = day.AddDays(1))
        {
            foreach (var step in data.Days.GetValueOrDefault(day) ?? [])
            {
                await step(ct);
            }

            await CloseDayAsync(day, ct);
            Report.Days++;
        }

        await VerifyAsync(startDate, lastDay, ct);
        return Report;
    }

    private sealed record Prepared(IReadOnlyList<SourceCustomer> Customers, IReadOnlyList<SourceAccount> Accounts, Dictionary<DateOnly, List<Func<CancellationToken, Task>>> Days);

    private Prepared Prepare(SourceData source, DateOnly startDate)
    {
        // The two-year window comes first, over the whole dataset, so a customer counts as "with a loan" only for a loan
        // that will be imported: one that starts inside the window.
        var allDates = source.Transactions.Select(t => t.Date).Concat(source.Loans.Select(l => l.StartDate))
            .Concat(source.LoanPayments.Select(p => p.Date)).Concat(source.StandingOrders.Select(o => o.StartDate)).ToList();
        var latest = allDates.DefaultIfEmpty(startDate).Max();
        var cutoff = latest.AddYears(-MaxYears);
        var keptLoans = source.Loans.Where(l => l.StartDate >= cutoff).ToDictionary(l => l.Id, l => l.CustomerId);
        var lateCustomers = source.LoanPayments.Where(p => p.Late && p.Date >= cutoff && keptLoans.ContainsKey(p.LoanId)).Select(p => keptLoans[p.LoanId]).ToHashSet();
        var loanCustomers = keptLoans.Values.ToHashSet();
        var accountOwner = source.Accounts.ToDictionary(a => a.Id, a => a.CustomerId);
        var orderCustomers = source.StandingOrders.Where(o => o.StartDate >= cutoff).Select(o => accountOwner.GetValueOrDefault(o.AccountId)).ToHashSet();
        var candidates = source.Customers.Select(c => new Candidate(c.Id, loanCustomers.Contains(c.Id), lateCustomers.Contains(c.Id), orderCustomers.Contains(c.Id))).ToList();
        var targets = new SelectionTargets(options.Customers, 30, 10, 15);
        var selected = Logic.Selection.Select(candidates, targets, options.Seed).ToHashSet();
        var chosen = candidates.Where(c => selected.Contains(c.Id)).ToList();
        void Reached(string what, int count, int target) =>
            Report.Selection.Add($"{what}: {count} (target {target}){(count < target ? " — BELOW TARGET: the dataset has too few inside the window" : "")}");
        Reached("customers", chosen.Count, targets.Total);
        Reached("with a loan", chosen.Count(c => c.HasLoan), targets.WithLoan);
        Reached("with late payments", chosen.Count(c => c.HasLatePayments), targets.WithLatePayments);
        Reached("with standing orders", chosen.Count(c => c.HasStandingOrder), targets.WithStandingOrder);
        var selectedAccounts = source.Accounts.Where(a => selected.Contains(a.CustomerId)).ToList();
        var accountIds = selectedAccounts.Select(a => a.Id).ToHashSet();

        var transactions = source.Transactions.Where(t => accountIds.Contains(t.AccountId)).ToList();
        var selectedLoans = source.Loans.Where(l => selected.Contains(l.CustomerId) && accountIds.Contains(l.AccountId)).ToList();
        var loanIds = selectedLoans.Select(l => l.Id).ToHashSet();
        var payments = source.LoanPayments.Where(p => loanIds.Contains(p.LoanId)).ToList();
        var orders = source.StandingOrders.Where(o => accountIds.Contains(o.AccountId)).ToList();

        var dates = transactions.Select(t => t.Date).Concat(selectedLoans.Select(l => l.StartDate)).Concat(payments.Select(p => p.Date)).Concat(orders.Select(o => o.StartDate)).ToList();
        // Shifted from the selected history's first kept day, never earlier than the dataset-wide cut-off.
        var shift = dates.Count == 0 ? new DateShift(startDate, 0) : DateShift.Compute(dates.Min(), latest, startDate, MaxYears);
        var days = new Dictionary<DateOnly, List<Func<CancellationToken, Task>>>();
        void On(DateOnly date, Func<CancellationToken, Task> step, string skippedWhy)
        {
            if (!shift.Keeps(date))
            {
                Report.Skipped.Add(skippedWhy + ": older than two years");
                return;
            }

            var day = shift.Apply(date);
            (days.TryGetValue(day, out var list) ? list : days[day] = []).Add(step);
        }

        // Within a day: loans first, so money disbursed that day can be spent that day; then transactions and standing orders.
        foreach (var l in selectedLoans)
        {
            On(l.StartDate, ct => LoanAsync(l, ct), $"loan {l.Id}");
        }

        foreach (var t in transactions.OrderBy(t => t.Date).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            On(t.Date, ct => TransactionAsync(t, accountIds, ct), $"transaction {t.Id}");
        }

        foreach (var o in orders)
        {
            On(o.StartDate, ct => StandingOrderAsync(o, shift, ct), $"standing order {o.Id}");
        }

        foreach (var p in payments.OrderBy(p => p.Date))
        {
            On(p.Date, ct => LoanPaymentAsync(p, ct), $"loan payment {p.Id}");
        }

        return new Prepared([.. source.Customers.Where(c => selected.Contains(c.Id))], selectedAccounts, days);
    }

    private async Task RegisterAsync(Prepared data, CancellationToken ct)
    {
        foreach (var c in data.Customers)
        {
            var response = await SendAsync(HttpMethod.Post, "/api/customers/customers", new
            {
                name = $"{c.FirstName} {c.LastName}".Trim(), nationalId = $"IMP-{c.Id}", dateOfBirth = c.DateOfBirth, email = c.Email,
                phone = string.IsNullOrWhiteSpace(c.Phone) ? "-" : c.Phone, address = string.IsNullOrWhiteSpace(c.Address) ? "-" : c.Address,
            }, null, ct);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                customers[c.Id] = (await response.Content.ReadFromJsonAsync<IdOnly>(Json, ct))!.Id;
                Report.Customers++;
            }
            else if (response.StatusCode == HttpStatusCode.Conflict && !options.Force)
            {
                // The customer is already in the bank: an earlier import ran. Stop before anything else is written.
                throw new InvalidOperationException(
                    $"Customer {c.Id} already exists (national id IMP-{c.Id}): this bank already holds imported customers. Import into a fresh database, or pass --force.");
            }
            else
            {
                Report.Skipped.Add($"customer {c.Id}: {(int)response.StatusCode} {await Code(response, ct)}");
            }
        }

        foreach (var a in data.Accounts.Where(a => customers.ContainsKey(a.CustomerId)))
        {
            if (AccountTypes.Map(a.Type) is not { } type)
            {
                Report.Skipped.Add($"account {a.Id}: unknown account type '{a.Type}'");
                continue;
            }

            // CustomerRegistered reaches Accounts a moment later: retry customer_not_found (accounts ADR 0001).
            var response = await RetryAsync(() => SendAsync(HttpMethod.Post, "/api/accounts/accounts", new { customerId = customers[a.CustomerId], type, currency = "SEK" }, null, ct), ct);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                accounts[a.Id] = (await response.Content.ReadFromJsonAsync<IdOnly>(Json, ct))!.Id;
                Report.Accounts++;
            }
            else
            {
                Report.Skipped.Add($"account {a.Id}: {(int)response.StatusCode} {await Code(response, ct)}");
            }
        }
    }

    private async Task TransactionAsync(SourceTransaction t, HashSet<string> imported, CancellationToken ct)
    {
        if (!accounts.TryGetValue(t.AccountId, out var account))
        {
            return;
        }

        var amount = Amounts.ToSek(t.Amount, options.AmountFactor);
        var key = IdempotencyKeys.For("transactions", t.Id);
        var (path, body, what) = t.Kind switch
        {
            "deposit" => ($"/api/accounts/accounts/{account}/deposits", (object)new { amount, description = "Imported deposit" }, "deposits"),
            "withdrawal" => ($"/api/accounts/accounts/{account}/withdrawals", new { amount, description = "Imported withdrawal" }, "withdrawals"),
            "transfer" when t.TargetAccountId is { } target && accounts.TryGetValue(target, out var to) =>
                ("/api/accounts/transfers", new { fromAccountId = account, toAccountId = to, amount, description = "Imported transfer" }, "transfers"),
            // A transfer to an account outside the import leaves the bank: a withdrawal.
            "transfer" => ($"/api/accounts/accounts/{account}/withdrawals", new { amount, description = "Imported external transfer" }, "external transfers"),
            _ => ("", new { }, ""),
        };
        if (path == "")
        {
            Report.Skipped.Add($"transaction {t.Id}: unknown kind '{t.Kind}'");
            return;
        }

        await ApplyAsync(HttpMethod.Post, path, body, key, what, $"transaction {t.Id}", ct);
    }

    private async Task StandingOrderAsync(SourceStandingOrder o, DateShift shift, CancellationToken ct)
    {
        if (!accounts.TryGetValue(o.AccountId, out var from) || !accounts.TryGetValue(o.TargetAccountId, out var to))
        {
            Report.Skipped.Add($"standing order {o.Id}: an account is not imported");
            return;
        }

        if (Frequencies.Map(o.Frequency) is not { } frequency)
        {
            Report.Skipped.Add($"standing order {o.Id}: unsupported frequency '{o.Frequency}'");
            return;
        }

        await ApplyAsync(HttpMethod.Post, "/api/accounts/standing-orders", new
        {
            fromAccountId = from, toAccountId = to, amount = Amounts.ToSek(o.Amount, options.AmountFactor), frequency,
            startDate = shift.Apply(o.StartDate), endDate = o.EndDate is { } end ? shift.Apply(end) : (DateOnly?)null,
        }, IdempotencyKeys.For("standing_orders", o.Id), "standing orders", $"standing order {o.Id}", ct);
    }

    private async Task LoanAsync(SourceLoan l, CancellationToken ct)
    {
        if (!customers.TryGetValue(l.CustomerId, out var customer) || !accounts.TryGetValue(l.AccountId, out var account))
        {
            Report.Skipped.Add($"loan {l.Id}: customer or account not imported");
            return;
        }

        var product = LoanTypes.Map(l.Type);
        var principal = Amounts.ToSek(l.Amount, options.AmountFactor);
        var today = (await CurrentDayAsync(ct)).Date;
        // The stage cap is the product's (no hard-coded caps); a product without one takes a single stage.
        var stages = product == "PURCHASE" ? StagePlanner.Equal(principal, today, l.TermMonths, maxStages.GetValueOrDefault(product) ?? 1).Select(s => new { dueDate = s.DueDate, principalShare = s.Share }).ToList() : null;
        var submitted = await RetryAsync(() => SendAsync(HttpMethod.Post, "/api/lending/loan-applications", new
        {
            customerId = customer, productCode = product, amount = principal, termMonths = l.TermMonths, repaymentAccountId = account,
            purpose = $"Imported {l.Type}".Trim(), stages,
        }, null, ct), ct);
        if (submitted.StatusCode != HttpStatusCode.Created)
        {
            // Amounts or terms outside the product are skipped, never clamped (stage 7 plan).
            Report.Skipped.Add($"loan {l.Id} ({product} {principal}, {l.TermMonths} months): {await Code(submitted, ct)}");
            return;
        }

        var applicationId = (await submitted.Content.ReadFromJsonAsync<IdOnly>(Json, ct))!.Id;
        foreach (var action in new[] { "review", "approve" })
        {
            await SendAsync(HttpMethod.Post, $"/api/lending/loan-applications/{applicationId}/{action}", new { }, null, ct);
        }

        var disbursed = await SendAsync(HttpMethod.Post, $"/api/lending/loan-applications/{applicationId}/disburse", new { }, null, ct);
        if (disbursed.StatusCode != HttpStatusCode.Created)
        {
            Report.Skipped.Add($"loan {l.Id}: disbursement {await Code(disbursed, ct)}");
            return;
        }

        loans[l.Id] = ((await disbursed.Content.ReadFromJsonAsync<IdOnly>(Json, ct))!.Id, account);
        Report.Count($"loans ({product})");
    }

    private async Task LoanPaymentAsync(SourceLoanPayment p, CancellationToken ct)
    {
        if (!loans.TryGetValue(p.LoanId, out var loan))
        {
            Report.Skipped.Add($"loan payment {p.Id}: loan not imported");
            return;
        }

        // Paid into the repayment account; automatic collection then pays the installment when it is due (stage 7 file, step 5).
        var amount = Amounts.ToSek(p.Amount, options.AmountFactor);
        var schedule = await gateway.GetFromJsonAsync<LoanView>($"/api/lending/loans/{loan.LoanId}", Json, ct);
        var installment = schedule?.Schedule.FirstOrDefault(i => i.Status != "Paid");
        if (installment is not null && installment.PrincipalDue.Amount + installment.InterestDue.Amount != amount.Amount)
        {
            Report.Mismatches.Add($"payment {p.Id} of {amount} for loan {p.LoanId}; next installment is {installment.PrincipalDue.Amount + installment.InterestDue.Amount} SEK");
        }

        await ApplyAsync(HttpMethod.Post, $"/api/accounts/accounts/{loan.AccountId}/deposits", new { amount, description = "Imported loan payment" },
            IdempotencyKeys.For("loan_payments", p.Id), "loan payment deposits", $"loan payment {p.Id}", ct);
    }

    private async Task ApplyAsync(HttpMethod method, string path, object body, string key, string what, string row, CancellationToken ct)
    {
        // The same Idempotency-Key on every attempt, so a retry can never apply a movement twice.
        var response = await RetryAsync(() => SendAsync(method, path, body, key, ct), ct);
        if (response.IsSuccessStatusCode)
        {
            Report.Count(what);
        }
        else
        {
            Report.Skipped.Add($"{row}: {(int)response.StatusCode} {await Code(response, ct)}");
        }
    }

    /// <summary>Closes the business day and polls until the next one is open (ADR 0001): every participant has completed it.</summary>
    private async Task CloseDayAsync(DateOnly day, CancellationToken ct)
    {
        await SendAsync(HttpMethod.Post, "/api/operations/business-days/current/close", new { }, null, ct);
        var deadline = DateTime.UtcNow + (options.DayTimeout ?? TimeSpan.FromMinutes(2));
        while (true)
        {
            var current = await CurrentDayAsync(ct);
            if (current.Date > day && current.Status == "Open")
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Business day {day:yyyy-MM-dd} did not complete in time (now {current.Date:yyyy-MM-dd} {current.Status}).");
            }

            await Task.Delay(200, ct);
        }
    }

    /// <summary>
    /// Verification runs until the services have caught up with the last day (their clocks and the last collections
    /// arrive through Kafka a moment after Operations opened the next day), for up to a minute; then the last differences count.
    /// </summary>
    private async Task VerifyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        while (true)
        {
            Report.VerificationErrors.Clear();
            await VerifyOnceAsync(from, to, ct);
            if (Report.Verified || DateTime.UtcNow > deadline)
            {
                return;
            }

            await Task.Delay(500, ct);
        }
    }

    private async Task VerifyOnceAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (accounts.Count == 0)
        {
            Report.VerificationErrors.Add("no account was imported, so nothing was verified");
        }

        foreach (var (source, id) in accounts)
        {
            var account = await gateway.GetFromJsonAsync<AccountView>($"/api/accounts/accounts/{id}", Json, ct);
            var response = await gateway.GetAsync($"/api/accounts/accounts/{id}/statement?from={from:yyyy-MM-dd}&to={to.AddDays(1):yyyy-MM-dd}", ct);
            if (!response.IsSuccessStatusCode)
            {
                Report.VerificationErrors.Add($"account {source}: statement {(int)response.StatusCode} {await Code(response, ct)}");
                continue;
            }

            var statement = await response.Content.ReadFromJsonAsync<StatementView>(Json, ct);
            var sum = statement!.OpeningBalance.Amount + statement.Lines.Sum(l => l.Amount.Amount);
            if (sum != account!.Balance.Amount || statement.ClosingBalance.Amount != account.Balance.Amount)
            {
                Report.VerificationErrors.Add($"account {source}: balance {account.Balance.Amount}, statement sum {sum}");
            }
        }

        foreach (var currency in new[] { "SEK", "EUR", "USD" })
        {
            var balance = await gateway.GetFromJsonAsync<TrialBalanceView>($"/api/ledger/trial-balance?currency={currency}", Json, ct);
            if (balance!.TotalDebit != balance.TotalCredit)
            {
                Report.VerificationErrors.Add($"trial balance {currency}: debit {balance.TotalDebit} ≠ credit {balance.TotalCredit}");
            }
        }
    }

    /// <summary>
    /// Codes that mean "the other service has not received the event yet" (eventual consistency between services): the
    /// call is retried for up to a minute before it counts as skipped.
    /// </summary>
    private static readonly HashSet<string> NotYet = ["customer_not_found", "currency_not_active", "repayment_account_invalid", "date_in_future"];

    private async Task<HttpResponseMessage> RetryAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        while (true)
        {
            var response = await send();
            var problem = (int)response.StatusCode is 400 or 422 ? await Code(response, ct) : null;
            if (problem is null || !NotYet.Contains(problem) || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(250, ct);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        var response = await gateway.SendAsync(request, ct);
        await response.Content.LoadIntoBufferAsync(ct);
        return response;
    }

    private async Task<(DateOnly Date, string Status)> CurrentDayAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/operations/business-days/current");
        var day = await (await gateway.SendAsync(request, ct)).Content.ReadFromJsonAsync<DayView>(Json, ct);
        return (day!.Date, day.Status);
    }

    private static async Task<string> Code(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "" : response.StatusCode.ToString();
        }
        catch (JsonException)
        {
            return response.StatusCode.ToString();
        }
    }

    private const int MaxYears = 2;

    private sealed record IdOnly(Guid Id);
    private sealed record ProductView(string Code, int? MaxStages);
    private sealed record DayView(DateOnly Date, string Status);
    private sealed record AccountView(Money Balance);
    private sealed record LineView(Money Amount);
    private sealed record StatementView(Money OpeningBalance, List<LineView> Lines, Money ClosingBalance);
    private sealed record TrialBalanceView(decimal TotalDebit, decimal TotalCredit);
    private sealed record ItemView(Money PrincipalDue, Money InterestDue, string Status);
    private sealed record LoanView(List<ItemView> Schedule);
}
