namespace Bank.Contracts;

public sealed class CurrencyMismatchException(string left, string right)
    : InvalidOperationException($"Cannot combine amounts in {left} and {right}.")
{
    public string Left { get; } = left;

    public string Right { get; } = right;
}
