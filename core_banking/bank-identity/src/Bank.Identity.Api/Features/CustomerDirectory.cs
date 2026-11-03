using System.Net.Http.Json;

namespace Bank.Identity.Api.Features;

public sealed record CustomerFound(Guid Id, string Name, string Status);

/// <summary>
/// Finds a customer by national id through the Customers service's staff search (POST /customers/search). A direct
/// HTTP call between services, allowed for this lookup only (workspace ADR 0007): Customers owns the national id and no
/// event carries it. Service discovery resolves http://customers. The national id goes in the body, never in a URL or log.
/// </summary>
public sealed class CustomerDirectory(HttpClient http)
{
    /// <summary>The actor Customers records for the lookup (X-Performed-By; the search is staff only, ADR 0006).</summary>
    public const string Actor = "identity";

    private sealed record Page(List<CustomerFound> Items);

    /// <summary>The customer, or null when no customer has this national id. Throws when Customers fails or does not answer.</summary>
    public async Task<CustomerFound?> FindByNationalIdAsync(string nationalId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/customers/search") { Content = JsonContent.Create(new { nationalId, limit = 1 }) };
        request.Headers.Add("X-Performed-By", Actor);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<Page>(ct);
        return page?.Items.SingleOrDefault();
    }
}
