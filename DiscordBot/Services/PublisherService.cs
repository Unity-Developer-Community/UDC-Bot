using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using DiscordBot.Settings;
using MailKit.Net.Smtp;
using MimeKit;

namespace DiscordBot.Services;

public class PublisherService
{
    private readonly BotSettings _settings;
    private readonly Dictionary<uint, string> _verificationCodes;

    public PublisherService(BotSettings settings)
    {
        _verificationCodes = new Dictionary<uint, string>();
        _settings = settings;
    }

    /*
        blogfeed (xml) => https://blogs.unity3d.com/feed/
    */

    // Attempts to get a publishers email from the unity asset store and emails them with confirmation codes to verify their account.
    public async Task<(bool, string)> VerifyPublisher(uint publisherId, string name)
    {
        // Unity is 1, we probably don't want to email them.
        if (publisherId < 2)
            return (false, "Invalid publisher ID.");

        using (var webClient = new WebClient())
        {
            // For the record, this is a terrible way of pulling this information.
            var content = await webClient.DownloadStringTaskAsync($"https://assetstore.unity.com/publishers/{publisherId}");
            if (!content.Contains("Error 404"))
            {
                var email = string.Empty;
                var emailMatch = new Regex("mailto:([^\"]+)").Match(content);
                if (emailMatch.Success)
                    email = emailMatch.Groups[1].Value;

                if (email.Length > 2)
                {
                    // No easy way to take their name, so we pass their discord name in.
                    await SendVerificationCode(name, email, publisherId);
                    return (true, "An email with a validation code was sent.\nPlease type `!verify <ID> <code>` to verify your publisher account.\nThis code will be valid for 30 minutes.");
                }
            }
            return (false, "We failed to confirm this Publisher ID, double check and try again in a few minutes.");
        }
    }

    public async Task SendVerificationCode(string name, string email, uint packageId)
    {
        var random = new byte[9];
        var rand = RandomNumberGenerator.Create();
        rand.GetBytes(random);

        var code = Convert.ToBase64String(random);

		var body = string.Join('\n', new string[] {
			$"Somebody named @{name} on the Unity Developer Community Discord server " +
			"has requested the Publisher role on that server.",
			"",
			$"Here's your validation code: {code}",
			"",
			$"If this is you, enter the command '!verify {packageID} {code}' to complete the process.",
			"",
			"If this is NOT you, feel free to join the UDC Discord server " +
			$"to report @{name} to the @Administrators (and join our community if you like).",
			"https://discord.gg/bu3bbby"
		});

        _verificationCodes[packageId] = code;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Unity Developer Community", _settings.Email));
        message.To.Add(new MailboxAddress(name, email));
        message.Subject = "Unity Developer Community Package Validation";
        message.Body = new TextPart("plain") { Text = body };

        using (var client = new SmtpClient())
        {
            client.CheckCertificateRevocation = false;
            await client.ConnectAsync(_settings.EmailSMTPServer, _settings.EmailSMTPPort, MailKit.Security.SecureSocketOptions.SslOnConnect);

            client.AuthenticationMechanisms.Remove("XOAUTH2");
            await client.AuthenticateAsync(_settings.EmailUsername, _settings.EmailPassword);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        //TODO Delete code after 30min
    }

    // User is verified if they have a code that matches the one in _verificationCodes and given `Asset-Publisher` role if so.
    public async Task<string> ValidatePublisherWithCode(IUser user, uint packageId, string code)
    {
        if (!_verificationCodes.TryGetValue(packageId, out string c))
            return "An error occurred while trying to verify your publisher account. Please check your ID is valid.";
        if (c != code)
            return "The verification code is not valid. Please check and try again.";
            
        // Give the user the publisher role.
        await ((SocketGuildUser)user)
            .AddRoleAsync(((SocketGuildUser)user)
                .Guild.GetRole(_settings.PublisherRoleId));
        // Remove this code since it is now used.
        _verificationCodes.Remove(packageId);
            
        return "Your publisher account has been verified and you now have the `Asset-Publisher` role!";
    }
}
