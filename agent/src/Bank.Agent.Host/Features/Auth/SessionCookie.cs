namespace Bank.Agent.Host.Features.Auth;

/// <summary>The opaque session cookie: HttpOnly so scripts cannot read it, Secure, SameSite=Strict.</summary>
public static class SessionCookie
{
    public const string Name = "bank_chat_session";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    // Browsers accept Secure cookies on http://localhost, so development needs no exception.
    public static void Write(HttpResponse response, string value) => response.Cookies.Append(Name, value, Options());

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options());

    private static CookieOptions Options() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };
}
