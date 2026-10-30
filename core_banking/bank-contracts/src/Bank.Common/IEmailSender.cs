namespace Bank.Common;

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends email: Mailpit locally, Mailpit or Amazon SES on AWS.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
