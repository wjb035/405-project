using MailKit.Net.Smtp;
using MimeKit;

namespace PGEmuBackend.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    public EmailService(IConfiguration config) { _config = config; }

    public async Task SendPasswordResetCodeAsync(string toEmail, string code)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_config["Email:From"]));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "PGEmu Password Reset Code";
            message.Body = new TextPart("plain")
            {
                Text = $"Your reset code is: {code}\n\nIt expires in 15 minutes."
            };

            using var client = new SmtpClient();

            await client.ConnectAsync(
                _config["Email:Host"],
                int.Parse(_config["Email:Port"]),
                MailKit.Security.SecureSocketOptions.None
            );
            
            // Only authenticate if credentials are configured
            var username = _config["Email:Username"];
            var password = _config["Email:Password"];
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                await client.AuthenticateAsync(username, password);
            
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Email send failed: {ex.Message}");
        }
       
    }
}