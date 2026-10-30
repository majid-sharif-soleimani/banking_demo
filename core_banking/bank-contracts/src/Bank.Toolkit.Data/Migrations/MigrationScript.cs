using System.Security.Cryptography;
using System.Text;

namespace Bank.Toolkit.Data.Migrations;

/// <param name="Id">Unique and stable, such as "messaging/0001_outbox_and_inbox.sql"; never rename an applied script.</param>
public sealed record MigrationScript(string Id, string Sql)
{
    /// <summary>SHA-256 of the script with line endings normalized, so a checkout on Windows gives the same value.</summary>
    /// <remarks>Computed on read, not stored: a stored value would be copied unchanged by <c>with { Sql = ... }</c>.</remarks>
    public string Checksum => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(Sql.ReplaceLineEndings("\n"))));
}
