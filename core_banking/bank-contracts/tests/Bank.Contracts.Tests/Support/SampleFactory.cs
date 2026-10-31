namespace Bank.Contracts.Tests.Support;

/// <summary>Builds an instance of an event record with valid sample values, through its primary constructor.</summary>
public static class SampleFactory
{
    // Fields whose schema has a pattern need a value that matches it.
    private static readonly Dictionary<string, object> ByParameterName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "SEK",
        ["Service"] = "accounts",
        ["PreferredLanguage"] = "en",
        ["AccountNumber"] = "123456782",
        ["AccountType"] = "Current",
        ["Currency"] = "SEK",
        ["FxRate"] = "11.50",
        ["Reason"] = "insufficient_funds",
    };

    public static object Create(Type recordType)
    {
        var constructor = recordType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(p => ValueFor(p.Name!, p.ParameterType)).ToArray();
        return constructor.Invoke(arguments);
    }

    private static object ValueFor(string name, Type type)
    {
        if (ByParameterName.TryGetValue(name, out var value))
        {
            return value;
        }

        var clrType = Nullable.GetUnderlyingType(type) ?? type;
        return clrType switch
        {
            _ when clrType == typeof(string) => "sample",
            _ when clrType == typeof(Guid) => Guid.CreateVersion7(),
            _ when clrType == typeof(DateOnly) => new DateOnly(2026, 10, 30),
            _ when clrType == typeof(DateTimeOffset) => new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
            _ when clrType == typeof(int) => 2,
            _ when clrType == typeof(long) => 2L,
            _ when clrType == typeof(bool) => true,
            _ when clrType == typeof(Money) => new Money(1234.50m, "SEK"),
            _ => throw new NotSupportedException($"No sample value for {clrType.Name} '{name}'; add one to {nameof(SampleFactory)}."),
        };
    }
}
