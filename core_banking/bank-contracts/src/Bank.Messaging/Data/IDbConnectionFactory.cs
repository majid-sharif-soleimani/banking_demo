using System.Data;

namespace Bank.Messaging.Data;

/// <summary>
/// Opens connections to the service's own database schema (ADR 0010). The only place that knows the database
/// provider; everything else sees <see cref="IDbConnection"/>.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>Opens a new connection; the caller disposes it.</summary>
    Task<IDbConnection> OpenAsync(CancellationToken ct);
}
