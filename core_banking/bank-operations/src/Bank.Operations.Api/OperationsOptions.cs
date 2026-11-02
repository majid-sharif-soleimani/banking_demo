using System.Text.RegularExpressions;

namespace Bank.Operations.Api;

/// <summary>Settings from the "Operations" configuration section. Nothing in the end-of-day cycle is hard-coded.</summary>
public sealed partial class OperationsOptions
{
    public const string Section = "Operations";

    /// <summary>
    /// The services that must send DayCompleted before the next day opens, such as ["accounts", "lending"].
    /// Starts empty: the configuration binder appends configured items to a default instead of replacing it.
    /// </summary>
    public string[] EndOfDayParticipants { get; set; } = [];

    /// <summary>The first business date, opened when the business_day table is empty.</summary>
    public DateOnly StartDate { get; set; } = new(2026, 1, 1);

    internal static IServiceCollection AddTo(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OperationsOptions>()
            .Bind(configuration.GetSection(Section))
            .Validate(o => o.EndOfDayParticipants.Length > 0,
                "Operations: EndOfDayParticipants must list at least one service, such as [\"accounts\", \"lending\"].")
            .Validate(o => o.EndOfDayParticipants.All(p => ServiceName().IsMatch(p)),
                "Operations: EndOfDayParticipants lists service names in lower case, such as \"accounts\" (DayCompleted.Service).")
            .Validate(o => o.EndOfDayParticipants.Distinct().Count() == o.EndOfDayParticipants.Length,
                "Operations: EndOfDayParticipants lists each service once.")
            .ValidateOnStart();
        return services;
    }

    // The same rule as DayCompleted.Service, so a configured participant can actually reply.
    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex ServiceName();
}
