namespace Bank.Agent.Host.Features.Auth;

/// <summary>
/// Refuses state-changing calls without the chat UI's own header. A form or link on another site cannot add a custom
/// header, and a script there needs a CORS preflight this host never allows: a second guard next to SameSite=Strict.
/// </summary>
public static class ChatClientHeader
{
    public const string Name = "X-Bank-Chat";

    public static IApplicationBuilder UseChatClientHeader(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var request = http.Request;
        var guarded = HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsDelete(request.Method);
        if (guarded && (request.Path.StartsWithSegments("/auth") || request.Path.StartsWithSegments("/agent"))
            && request.Headers[Name] != "1")
        {
            await Problems.MissingClientHeader().ExecuteAsync(http);
            return;
        }

        await next(http);
    });
}
