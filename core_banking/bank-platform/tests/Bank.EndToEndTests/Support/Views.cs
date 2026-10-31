using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bank.EndToEndTests.Support;

// The parts of each service's JSON the scenarios read. Kept here, not referenced from the services, so the tests see
// the API as an outside client does.

/// <summary>Money as the API writes it: <c>{ "amount": "1234.50", "currency": "SEK" }</c>, the amount a string (bank-contracts).</summary>
[JsonConverter(typeof(MoneyJson))]
public sealed record Money(decimal Amount, string Currency);

public sealed class MoneyJson : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return new Money(decimal.Parse(root.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture), root.GetProperty("currency").GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("amount", value.Amount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}

public sealed record BusinessDayView(DateOnly Date, string Status);

public sealed record CustomerView(Guid Id, string Email);

public sealed record AccountView(Guid Id, string Currency, Money Balance);

public sealed record EntryView(Guid EntryId, Money Amount, Money BalanceAfter, string Kind);

public sealed record TransferView(Guid TransferId, Money Amount, Money ConvertedAmount, decimal? FxRate);

public sealed record StatementLineView(DateOnly BusinessDate, string Kind, Money Amount, Money BalanceAfter);

public sealed record StatementView(Money OpeningBalance, List<StatementLineView> Lines, Money ClosingBalance);

public sealed record JournalLineView(int GlCode, decimal Debit, decimal Credit);

public sealed record JournalEntryView(Guid Id, string Currency, string SourceEvent, List<JournalLineView> Lines);

public sealed record TrialBalanceRowView(int GlCode, string Name, decimal Balance);

public sealed record TrialBalanceView(string Currency, List<TrialBalanceRowView> Rows, decimal TotalDebit, decimal TotalCredit)
{
    public decimal BalanceOf(int glCode) => Rows.SingleOrDefault(r => r.GlCode == glCode)?.Balance ?? 0m;
}

public sealed record ApplicationView(Guid Id, string Status, Guid? LoanId);

public sealed record IdView(Guid Id);

public sealed record ScheduleItemView(
    int Number, DateOnly DueDate, Money PrincipalDue, Money InterestDue, Money PenaltyDue, Money PaidPrincipal, Money PaidInterest,
    Money PaidPenalty, string Status)
{
    public decimal Unpaid => PrincipalDue.Amount + InterestDue.Amount + PenaltyDue.Amount - PaidPrincipal.Amount - PaidInterest.Amount - PaidPenalty.Amount;
}

public sealed record LoanView(
    Guid Id, string Status, string Classification, Money OutstandingPrincipal, Money OverdueAmount, int DaysPastDue, List<ScheduleItemView> Schedule);

public sealed record PayoffQuoteView(Money Total, Money WaivedInterest);

public sealed record InformationRequestView(Guid Id, string Status);

public sealed record InteractionView(Guid Id, string Kind, string? FileName);

public sealed record CardPaymentView(Guid Id, string Status);

public sealed record StandingOrderView(Guid Id, string Status);

/// <summary>Chart of accounts codes (bank-ledger ADR 0002).</summary>
public static class Gl
{
    public const int LoansReceivable = 1200;
    public const int CustomerDeposits = 2000;
}
