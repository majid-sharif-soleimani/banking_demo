using Bank.Operations.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bank.Operations.IntegrationTests;

/// <summary>The configuration rules that stop the service at startup instead of running an end of day nobody waits for.</summary>
public class OperationsOptionsTests
{
    [Fact]
    public void The_shipped_appsettings_are_valid()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();

        var options = Resolve(configuration);

        options.EndOfDayParticipants.ShouldBe(["accounts", "lending"]);
        options.StartDate.ShouldBe(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void Configured_participants_replace_the_default_instead_of_being_added()
    {
        var options = Resolve(Settings(("Operations:EndOfDayParticipants:0", "stub")));

        options.EndOfDayParticipants.ShouldBe(["stub"]);
    }

    [Fact]
    public void StartDate_is_read_from_configuration()
    {
        var options = Resolve(Settings(("Operations:EndOfDayParticipants:0", "accounts"), ("Operations:StartDate", "2027-03-15")));

        options.StartDate.ShouldBe(new DateOnly(2027, 3, 15));
    }

    [Fact]
    public void No_participants_fails() =>
        Should.Throw<OptionsValidationException>(() => Resolve(Settings()))
            .Message.ShouldContain("EndOfDayParticipants must list at least one service");

    [Theory]
    [InlineData("Accounts")]
    [InlineData("bank.accounts.events")]
    [InlineData("")]
    public void A_participant_that_is_not_a_lower_case_service_name_fails(string participant) =>
        Should.Throw<OptionsValidationException>(() => Resolve(Settings(("Operations:EndOfDayParticipants:0", participant))))
            .Message.ShouldContain("lower case");

    [Fact]
    public void A_participant_listed_twice_fails() =>
        Should.Throw<OptionsValidationException>(() => Resolve(Settings(
                ("Operations:EndOfDayParticipants:0", "accounts"), ("Operations:EndOfDayParticipants:1", "accounts"))))
            .Message.ShouldContain("each service once");

    private static IConfiguration Settings(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build();

    private static OperationsOptions Resolve(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        OperationsOptions.AddTo(services, configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<OperationsOptions>>().Value;
    }
}
