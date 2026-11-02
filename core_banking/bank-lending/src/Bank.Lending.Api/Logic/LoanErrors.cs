using Bank.Common;

namespace Bank.Lending.Api.Logic;

/// <summary>
/// The ways loan input can be invalid. Logic/ returns them in a Result instead of throwing (financial-logic rule;
/// ADR 0002); the endpoints in stage 5 turn them into 422 responses with the code.
/// </summary>
public static class LoanErrors
{
    public static readonly Error PrincipalNotPositive = new("principal_not_positive", "The principal must be greater than zero.");
    public static readonly Error TermNotPositive = new("term_not_positive", "The term must be at least one month.");
    public static readonly Error RateNegative = new("rate_negative", "The annual rate cannot be negative.");
    public static readonly Error WrongRepaymentType = new("wrong_repayment_type", "The loan terms have another repayment type.");
    public static readonly Error FirstDueNotAfterDisbursement = new("first_due_not_after_disbursement", "The first due date must be after the disbursement date.");
    public static readonly Error NoStages = new("no_stages", "A staged loan needs at least one stage.");
    public static readonly Error TooManyStages = new("too_many_stages", "The plan has more stages than the product allows.");
    public static readonly Error SharesDoNotSumToPrincipal = new("shares_do_not_sum_to_principal", "The principal shares must sum exactly to the principal.");
    public static readonly Error ShareNotPositive = new("share_not_positive", "Every principal share must be greater than zero.");
    public static readonly Error DatesNotIncreasing = new("dates_not_increasing", "Stage dates must be after the disbursement date and strictly increasing.");
    public static readonly Error StagePastTerm = new("stage_past_term", "A stage falls after the end of the term.");
    public static readonly Error CurrencyMismatch = new("currency_mismatch", "Every amount must be in the loan currency.");
    public static readonly Error AmountNotInMinorUnits = new("amount_not_in_minor_units", "An amount has more decimals than the currency allows.");
    public static readonly Error InvalidTerms = new("invalid_terms", "Penalty rate, grace days, classification thresholds or payoff percent are out of range.");
    public static readonly Error PaymentNotPositive = new("payment_not_positive", "The payment must be greater than zero.");
}
