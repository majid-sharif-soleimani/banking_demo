namespace Bank.Common;

/// <summary>Creates new account numbers. The format is still an open question (docs/progress.md).</summary>
public interface IAccountNumberGenerator
{
    Task<string> NextAsync(CancellationToken ct);
}
