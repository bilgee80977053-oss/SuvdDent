
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DentalApi.Services;

public class EmailService(IConfiguration config)
{
    public async Task SendAsync(
        string toEmail,
        string subject,
        string body)
    {
        var message = new MimeMessage();

        message.From.Add(
            MailboxAddress.Parse(config["Email:From"]!));

        message.To.Add(
            MailboxAddress.Parse(toEmail));

        message.Subject = subject;

        message.Body = new TextPart("plain")
        {
            Text = body
        };

        using var client = new SmtpClient();

        await client.ConnectAsync(
            config["Email:Host"]!,
            int.Parse(config["Email:Port"]!),
            SecureSocketOptions.StartTls);

        await client.AuthenticateAsync(
            config["Email:Username"]!,
            config["Email:Password"]!);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
