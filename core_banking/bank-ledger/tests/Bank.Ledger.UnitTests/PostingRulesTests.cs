using Bank.Contracts;
using Bank.Ledger.Api.Logic;
using static Bank.Ledger.Api.Logic.GlCode;

namespace Bank.Ledger.UnitTests;

public class PostingRulesTests
{
    private static Money Sek(decimal v) => new(v, "SEK");

    private static (GlCode, decimal, decimal)[] Lines(JournalDraft draft) => [.. draft.Lines.Select(l => (l.Code, l.Debit, l.Credit))];

    [Fact]
    public void Deposit_DebitsCashAndCreditsCustomerDeposits() =>
        Lines(PostingRules.Deposit(Sek(1_000m)).ShouldHaveSingleItem()).ShouldBe([(Cash, 1_000m, 0m), (CustomerDeposits, 0m, 1_000m)]);

    [Fact]
    public void Withdrawal_DebitsCustomerDepositsAndCreditsCash() =>
        Lines(PostingRules.Withdrawal(Sek(200m)).ShouldHaveSingleItem()).ShouldBe([(CustomerDeposits, 200m, 0m), (Cash, 0m, 200m)]);

    [Fact]
    public void Transfer_SameCurrency_PostsNothing() => PostingRules.Transfer(Sek(400m), Sek(400m)).ShouldBeEmpty();

    [Fact]
    public void Transfer_TwoCurrencies_PostsOneBalancedEntryPerCurrencyThroughFxPosition()
    {
        // 100 EUR → 1,150 SEK: EUR entry D CustomerDeposits / C FxPosition 100; SEK entry D FxPosition / C CustomerDeposits 1,150.
        var drafts = PostingRules.Transfer(new Money(100m, "EUR"), Sek(1_150m));

        drafts.Select(d => d.Currency).ShouldBe(["EUR", "SEK"]);
        Lines(drafts[0]).ShouldBe([(CustomerDeposits, 100m, 0m), (FxPosition, 0m, 100m)]);
        Lines(drafts[1]).ShouldBe([(FxPosition, 1_150m, 0m), (CustomerDeposits, 0m, 1_150m)]);
    }

    [Fact]
    public void Disbursement_DebitsLoansReceivableAndCreditsCustomerDeposits() =>
        Lines(PostingRules.Disbursement(Sek(200_000m)).ShouldHaveSingleItem()).ShouldBe([(LoansReceivable, 200_000m, 0m), (CustomerDeposits, 0m, 200_000m)]);

    [Theory]
    [InlineData("Account", CustomerDeposits)]
    [InlineData("Payoff", CustomerDeposits)]
    [InlineData("Card", CardClearing)]
    public void PaymentAllocated_DebitsTheSourceAndCreditsEachPart(string source, GlCode debit)
    {
        // 6,173.94 = principal 5,012.52 + interest 1,158.33 + penalty 3.09
        var draft = PostingRules.PaymentAllocated(source, Sek(5_012.52m), Sek(1_158.33m), Sek(3.09m)).ShouldHaveSingleItem();

        Lines(draft).ShouldBe([(debit, 6_173.94m, 0m), (LoansReceivable, 0m, 5_012.52m), (InterestIncome, 0m, 1_158.33m), (PenaltyIncome, 0m, 3.09m)]);
    }

    [Fact]
    public void PaymentAllocated_ZeroParts_GetNoLine() =>
        Lines(PostingRules.PaymentAllocated("Account", Sek(100m), Sek(0m), Sek(0m)).ShouldHaveSingleItem())
            .ShouldBe([(CustomerDeposits, 100m, 0m), (LoansReceivable, 0m, 100m)]);

    [Fact]
    public void PaymentAllocated_NothingAllocated_PostsNothing() =>
        PostingRules.PaymentAllocated("Card", Sek(0m), Sek(0m), Sek(0m)).ShouldBeEmpty();

    [Fact]
    public void PaymentAllocated_UnknownSource_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => PostingRules.PaymentAllocated("Cash", Sek(1m), Sek(0m), Sek(0m)));

    public static TheoryData<string> AllRules() => ["deposit", "withdrawal", "transfer", "disbursement", "account", "card", "payoff"];

    [Theory]
    [MemberData(nameof(AllRules))]
    public void EveryEntry_BalancesPerCurrency(string rule)
    {
        var drafts = rule switch
        {
            "deposit" => PostingRules.Deposit(Sek(12.34m)),
            "withdrawal" => PostingRules.Withdrawal(Sek(12.34m)),
            "transfer" => PostingRules.Transfer(new Money(10.85m, "USD"), new Money(10m, "EUR")),
            "disbursement" => PostingRules.Disbursement(Sek(90_000m)),
            "account" => PostingRules.PaymentAllocated("Account", Sek(30_000m), Sek(1_886.30m), Sek(13.55m)),
            "card" => PostingRules.PaymentAllocated("Card", Sek(1m), Sek(2m), Sek(3m)),
            _ => PostingRules.PaymentAllocated("Payoff", Sek(194_987.48m), Sek(2_527.01m), Sek(0m)),
        };

        drafts.ShouldNotBeEmpty();
        drafts.ShouldAllBe(d => d.IsBalanced && d.Lines.All(l => (l.Debit == 0m) != (l.Credit == 0m)));
    }

    [Fact]
    public void Draft_WithUnevenLines_IsNotBalanced() =>
        new JournalDraft("SEK", "x", [new(Cash, 10m, 0m), new(CustomerDeposits, 0m, 9.99m)]).IsBalanced.ShouldBeFalse();
}

public class TrialBalanceTests
{
    [Fact]
    public void From_SumsDebitsAndCreditsPerAccountAndBalances()
    {
        // Deposit 1,000; disbursement 200,000; payment 6,170.85 (5,012.52 principal + 1,158.33 interest).
        var lines = new[] { PostingRules.Deposit(new Money(1_000m, "SEK")), PostingRules.Disbursement(new Money(200_000m, "SEK")),
                PostingRules.PaymentAllocated("Account", new Money(5_012.52m, "SEK"), new Money(1_158.33m, "SEK"), new Money(0m, "SEK")) }
            .SelectMany(d => d).SelectMany(d => d.Lines);

        var balance = TrialBalance.From(lines);

        balance.TotalDebit.ShouldBe(balance.TotalCredit);
        balance.Rows.Single(r => r.Code == GlCode.LoansReceivable).Balance.ShouldBe(200_000m - 5_012.52m);
        // Liability: 1,000 + 200,000 − 6,170.85 = 194,829.15 credit balance, shown positive for its side.
        balance.Rows.Single(r => r.Code == GlCode.CustomerDeposits).Balance.ShouldBe(194_829.15m);
        balance.Rows.Single(r => r.Code == GlCode.InterestIncome).Balance.ShouldBe(1_158.33m);
    }

    [Fact]
    public void Chart_HasTheSevenAccountsOfTheStageFile() =>
        GlAccounts.Chart.Select(a => ((int)a.Code, a.Name, a.Type)).ShouldBe([
            (1000, "Cash", GlAccountType.Asset), (1100, "CardClearing", GlAccountType.Asset), (1200, "LoansReceivable", GlAccountType.Asset),
            (2000, "CustomerDeposits", GlAccountType.Liability), (2900, "FxPosition", GlAccountType.Liability),
            (4000, "InterestIncome", GlAccountType.Income), (4100, "PenaltyIncome", GlAccountType.Income)]);
}
