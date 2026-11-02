
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DentalApi.Services;

public class EmailService(IConfiguration config)
{
    public async Task SendAsync(
        string toEmail,
        string subject,
        string body,
        string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();

        message.From.Add(
            MailboxAddress.Parse(config["Email:From"]!));

        message.To.Add(
            MailboxAddress.Parse(toEmail));

        message.Subject = subject;
        if (messageId is not null) message.MessageId = messageId;

        message.Body = new TextPart("plain")
        {
            Text = body
        };

        using var client = new SmtpClient { Timeout = 15000 };

        await client.ConnectAsync(
            config["Email:Host"]!,
            int.Parse(config["Email:Port"]!),
            SecureSocketOptions.StartTls, cancellationToken);

        await client.AuthenticateAsync(
            config["Email:Username"]!,
            config["Email:Password"]!, cancellationToken);

        await client.SendAsync(message, cancellationToken);
        // SendAsync already received SMTP acceptance. A failed QUIT must not cause replay.
        try { await client.DisconnectAsync(true, cancellationToken); }
        catch (Exception) { }
    }
}
