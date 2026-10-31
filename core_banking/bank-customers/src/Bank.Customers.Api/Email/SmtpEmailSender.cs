using System.Net.Mail;
using Bank.Common;
using Microsoft.Extensions.Options;

namespace Bank.Customers.Api.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>The sender address of every customer email (a placeholder domain, never a real mailbox in the repo).</summary>
    public string From { get; set; } = "no-reply@bank.example";
}

/// <summary>
/// IEmailSender over SMTP: Mailpit locally (design plan ADR 19), SES's SMTP endpoint later. The host and port come from
/// the Aspire connection string "mailpit" (Endpoint=smtp://host:port).
/// </summary>
public sealed class SmtpEmailSender(SmtpEndpoint endpoint, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient(endpoint.Host, endpoint.Port);
        using var mail = new MailMessage(options.Value.From, message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, ct);
    }
}

public sealed record SmtpEndpoint(string Host, int Port)
{
    /// <summary>Reads "Endpoint=smtp://host:port" (Aspire) or a bare "smtp://host:port".</summary>
    public static SmtpEndpoint Parse(string connectionString)
    {
        var value = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase) ? part["Endpoint=".Length..] : part)
            .First();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "smtp" && uri.Port > 0
            ? new SmtpEndpoint(uri.Host, uri.Port)
            : throw new FormatException("ConnectionStrings:mailpit must look like Endpoint=smtp://host:port.");
    }
}
