using Bank.Contracts;

namespace Bank.Ledger.Api.Logic;

/// <summary>The chart of accounts (stage 6 file), one set per currency.</summary>
public enum GlCode
{
    Cash = 1000,
    CardClearing = 1100,
    LoansReceivable = 1200,
    CustomerDeposits = 2000,
    FxPosition = 2900,
    InterestIncome = 4000,
    PenaltyIncome = 4100,
}

public enum GlAccountType
{
    Asset,
    Liability,
    Income,
}

public sealed record GlAccountDefinition(GlCode Code, string Name, GlAccountType Type)
{
    /// <summary>Assets have debit balances; liabilities and income credit balances.</summary>
    public bool DebitNormal => Type == GlAccountType.Asset;
}

public static class GlAccounts
{
    public static readonly IReadOnlyList<GlAccountDefinition> Chart =
    [
        new(GlCode.Cash, "Cash", GlAccountType.Asset),
        new(GlCode.CardClearing, "CardClearing", GlAccountType.Asset),
        new(GlCode.LoansReceivable, "LoansReceivable", GlAccountType.Asset),
        new(GlCode.CustomerDeposits, "CustomerDeposits", GlAccountType.Liability),
        new(GlCode.FxPosition, "FxPosition", GlAccountType.Liability),
        new(GlCode.InterestIncome, "InterestIncome", GlAccountType.Income),
        new(GlCode.PenaltyIncome, "PenaltyIncome", GlAccountType.Income),
    ];

    public static GlAccountDefinition Of(GlCode code) => Chart.Single(a => a.Code == code);
}

/// <summary>One line of a journal entry: exactly one of debit and credit is non-zero.</summary>
public sealed record JournalLine(GlCode Code, decimal Debit, decimal Credit);

/// <summary>A journal entry before it is stored: one currency, balanced lines.</summary>
public sealed record JournalDraft(string Currency, string Description, IReadOnlyList<JournalLine> Lines)
{
    public bool IsBalanced => Lines.Count >= 2 && Lines.Sum(l => l.Debit) == Lines.Sum(l => l.Credit);
}

/// <summary>
/// The posting rules (stage 6 file, design plan section 6), one pure function per event (ADR 0001). Each returns the
/// entries to post, one per currency, every one balanced; an empty list means the event has no accounting effect.
/// Cash basis: interest and penalties are income only when collected, so waived interest never appears.
/// </summary>
public static class PostingRules
{
    public static IReadOnlyList<JournalDraft> Deposit(Money amount) =>
        [Entry(amount.Currency, "Cash deposit", Debit(GlCode.Cash, amount), Credit(GlCode.CustomerDeposits, amount))];

    public static IReadOnlyList<JournalDraft> Withdrawal(Money amount) =>
        [Entry(amount.Currency, "Cash withdrawal", Debit(GlCode.CustomerDeposits, amount), Credit(GlCode.Cash, amount))];

    /// <summary>Same currency: money moves between two customer deposits, which nets to nothing. Two currencies: through FxPosition.</summary>
    public static IReadOnlyList<JournalDraft> Transfer(Money amount, Money converted) =>
        amount.Currency == converted.Currency
            ? []
            : [
                Entry(amount.Currency, "Currency transfer (source)", Debit(GlCode.CustomerDeposits, amount), Credit(GlCode.FxPosition, amount)),
                Entry(converted.Currency, "Currency transfer (target)", Debit(GlCode.FxPosition, converted), Credit(GlCode.CustomerDeposits, converted)),
            ];

    public static IReadOnlyList<JournalDraft> Disbursement(Money principal) =>
        [Entry(principal.Currency, "Loan disbursement", Debit(GlCode.LoansReceivable, principal), Credit(GlCode.CustomerDeposits, principal))];

    /// <param name="source">Account or Payoff (paid from a customer deposit), or Card (through card clearing).</param>
    public static IReadOnlyList<JournalDraft> PaymentAllocated(string source, Money principal, Money interest, Money penalty)
    {
        var from = source switch
        {
            "Account" or "Payoff" => GlCode.CustomerDeposits,
            "Card" => GlCode.CardClearing,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown payment source."),
        };
        var total = principal + interest + penalty;
        if (total.Amount == 0m)
        {
            return [];
        }

        var lines = new List<JournalLine> { Debit(from, total) };
        lines.AddRange(new[] { Credit(GlCode.LoansReceivable, principal), Credit(GlCode.InterestIncome, interest), Credit(GlCode.PenaltyIncome, penalty) }
            .Where(l => l.Credit != 0m));
        return [new JournalDraft(total.Currency, $"Loan payment ({source})", lines)];
    }

    private static JournalDraft Entry(string currency, string description, params JournalLine[] lines) => new(currency, description, lines);

    private static JournalLine Debit(GlCode code, Money amount) => new(code, amount.Amount, 0m);

    private static JournalLine Credit(GlCode code, Money amount) => new(code, 0m, amount.Amount);
}

public sealed record TrialBalanceRow(GlCode Code, string Name, GlAccountType Type, decimal Debit, decimal Credit)
{
    /// <summary>The balance on the account's normal side: debit − credit for assets, credit − debit otherwise.</summary>
    public decimal Balance => GlAccounts.Of(Code).DebitNormal ? Debit - Credit : Credit - Debit;
}

public sealed record TrialBalanceResult(IReadOnlyList<TrialBalanceRow> Rows)
{
    public decimal TotalDebit => Rows.Sum(r => r.Debit);

    public decimal TotalCredit => Rows.Sum(r => r.Credit);
}

public static class TrialBalance
{
    /// <summary>Debits and credits per account, in chart order, for the lines of one currency.</summary>
    public static TrialBalanceResult From(IEnumerable<JournalLine> lines)
    {
        var sums = lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => (Debit: g.Sum(l => l.Debit), Credit: g.Sum(l => l.Credit)));
        return new([.. GlAccounts.Chart.Select(a => sums.TryGetValue(a.Code, out var s)
            ? new TrialBalanceRow(a.Code, a.Name, a.Type, s.Debit, s.Credit)
            : new TrialBalanceRow(a.Code, a.Name, a.Type, 0m, 0m))]);
    }
}
