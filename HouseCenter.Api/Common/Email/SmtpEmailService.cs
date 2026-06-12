using HouseCenter.Api.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HouseCenter.Api.Common.Email;

public sealed class SmtpEmailService(
    IOptions<EmailOptions> emailOptions,
    ILogger<SmtpEmailService> logger) : IEmailService
{
    private readonly EmailOptions _email = emailOptions.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Dev convenience: with no SMTP host configured, log instead of failing
        if (string.IsNullOrWhiteSpace(_email.Host))
        {
            logger.LogWarning("SMTP not configured — email to {To} with subject '{Subject}' was NOT sent. Body:\n{Body}",
                to, subject, htmlBody);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_email.FromName, _email.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_email.Host, _email.Port, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(_email.UserName, _email.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
    }
}
