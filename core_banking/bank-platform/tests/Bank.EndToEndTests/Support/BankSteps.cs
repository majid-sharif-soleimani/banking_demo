namespace Bank.EndToEndTests.Support;

/// <summary>The steps several scenarios share, each one a call or two through the gateway.</summary>
public static class BankSteps
{
    /// <summary>Staff register the customer; the national id is remembered so the customer can log in with it.</summary>
    public static async Task<CustomerView> RegisterCustomerAsync(this BankFixture bank, string name, string email)
    {
        var nationalId = $"E2E-{Guid.NewGuid():N}"[..20];
        var customer = await bank.Staff.PostAsync<CustomerView>("/api/customers/customers", new
        {
            name, nationalId, dateOfBirth = new DateOnly(1985, 4, 12), email,
            phone = "+46 70 000 00 00", address = "Example Street 1", preferredLanguage = "en",
        });
        bank.RememberNationalId(customer.Id, nationalId);
        return customer;
    }

    /// <summary>The customer opens an account; Accounts learns about the customer from CustomerRegistered, so it may need a moment.</summary>
    public static async Task<AccountView> OpenAccountAsync(this BankFixture bank, CustomerView customer, string currency, string type = "Current") =>
        await bank.Customer(customer.Id).PostWhenKnownAsync<AccountView>("/api/accounts/accounts", new { customerId = customer.Id, type, currency });

    public static async Task<EntryView> DepositAsync(this BankFixture bank, AccountView account, decimal amount) =>
        await bank.Staff.PostAsync<EntryView>($"/api/accounts/accounts/{account.Id}/deposits",
            new { amount = new Money(amount, account.Currency), description = "Cash deposit" }, idempotent: true);

    public static async Task<EntryView> WithdrawAsync(this BankFixture bank, AccountView account, decimal amount) =>
        await bank.Staff.PostAsync<EntryView>($"/api/accounts/accounts/{account.Id}/withdrawals",
            new { amount = new Money(amount, account.Currency), description = "Cash withdrawal" }, idempotent: true);

    public static Task<AccountView> AccountAsync(this BankFixture bank, AccountView account) =>
        bank.Staff.GetAsync<AccountView>($"/api/accounts/accounts/{account.Id}");

    public static Task<LoanView> LoanAsync(this BankFixture bank, Guid loanId) => bank.Staff.GetAsync<LoanView>($"/api/lending/loans/{loanId}");

    public static Task<TrialBalanceView> TrialBalanceAsync(this BankFixture bank, string currency) =>
        bank.Staff.GetAsync<TrialBalanceView>($"/api/ledger/trial-balance?currency={currency}");

    /// <summary>The customer applies for a loan paid into <paramref name="account"/> (Lending learns the account from AccountOpened).</summary>
    public static async Task<ApplicationView> ApplyForLoanAsync(this BankFixture bank, CustomerView customer, AccountView account, string product, decimal amount, int termMonths) =>
        await bank.Customer(customer.Id).PostWhenKnownAsync<ApplicationView>("/api/lending/loan-applications", new
        {
            customerId = customer.Id, productCode = product, amount = new Money(amount, "SEK"), termMonths, repaymentAccountId = account.Id,
            purpose = "End-to-end scenario", stages = (object?)null,
        });

    /// <summary>Staff review, approve and disburse an application; returns the new loan's id.</summary>
    public static async Task<Guid> ApproveAndDisburseAsync(this BankFixture bank, ApplicationView application, bool review = true)
    {
        if (review)
        {
            await bank.Staff.PostAsync<ApplicationView>($"/api/lending/loan-applications/{application.Id}/review");
        }

        await bank.Staff.PostAsync<ApplicationView>($"/api/lending/loan-applications/{application.Id}/approve");
        return (await bank.Staff.PostAsync<IdView>($"/api/lending/loan-applications/{application.Id}/disburse")).Id;
    }
}
