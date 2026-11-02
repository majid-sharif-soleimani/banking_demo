using Bank.Operations.Api.Data;

namespace Bank.Operations.Api.Features.BusinessDays;

public sealed record BusinessDayResponse(DateOnly Date, string Status)
{
    public static BusinessDayResponse From(BusinessDay day) => new(day.Date, day.Status.ToString());
}
