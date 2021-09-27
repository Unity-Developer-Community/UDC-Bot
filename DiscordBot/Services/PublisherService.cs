using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBot.Data;
using MailKit.Net.Smtp;
using MimeKit;
using Newtonsoft.Json;

namespace DiscordBot.Services
{
    public class PublisherService
    {
        private readonly DiscordSocketClient _client;

        private readonly Settings.Deserialized.Settings _settings;

        private readonly Dictionary<uint, string> _verificationCodes;

        public PublisherService(DiscordSocketClient client, Settings.Deserialized.Settings settings)
        {
            _client = client;
            _verificationCodes = new Dictionary<uint, string>();
            _settings = settings;
        }

        /*
        (No longer works) DailyObject => https://www.assetstore.unity3d.com/api/en-US/sale/results/10.json
        (No longer works) PackageOBject => https://www.assetstore.unity3d.com/api/en-US/content/overview/[PACKAGEID].json
        (No longer works) PriceObject (€) => https://www.assetstore.unity3d.com/api/en-US/content/price/[PACKAGEID].json
        (No longer works) PackageHead => https://www.assetstore.unity3d.com/api/en-US/head/package/[PACKAGEID].json
        blogfeed (xml) => https://blogs.unity3d.com/feed/
        */

        public async Task<DailyObject> GetDaily()
        {
            using (var httpClient = new HttpClient())
            {
                var json = await httpClient.GetStringAsync(
                    "https://www.assetstore.unity3d.com/api/en-US/sale/results/10.json");
                return JsonConvert.DeserializeObject<DailyObject>(json);
            }
        }

        public async Task<PackageObject> GetPackage(uint packageId)
        {
            using (var httpClient = new HttpClient())
            {
                var json = await httpClient.GetStringAsync(
                    $"https://www.assetstore.unity3d.com/api/en-US/content/overview/{packageId}.json");
                return JsonConvert.DeserializeObject<PackageObject>(json);
            }
        }

        public async Task<PackageHeadObject> GetPackageHead(uint packageId)
        {
            using (var httpClient = new HttpClient())
            {
                var json = await httpClient.GetStringAsync(
                    $"https://www.assetstore.unity3d.com/api/en-US/head/package/{packageId}.json");
                return JsonConvert.DeserializeObject<PackageHeadObject>(json);
            }
        }

        public async Task<PriceObject> GetPackagePrice(uint packageId)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                var json = await httpClient.GetStringAsync(
                    $"https://www.assetstore.unity3d.com/api/en-US/content/price/{packageId}.json");
                return JsonConvert.DeserializeObject<PriceObject>(json);
            }
        }

        public async Task<(string, Stream)> GetPublisherAdvertisting(ulong userid, PackageObject package,
                                                                     PackageHeadObject packageHead, PriceObject packagePrice)
        {
            var descStrippedHtml = Regex.Replace(package.Content.Description, "<.*?>", string.Empty);
            descStrippedHtml = Regex.Replace(descStrippedHtml, "&nbsp;", string.Empty);

            var sb = new StringBuilder();
            sb.Append("**--- Publisher everyday Advertising ---**\n\n");
            sb.Append($"Today's daily advertisting goes to {_client.GetUser(userid).Mention} (**{packageHead.Result.Publisher}**)\n");
            sb.Append($"With their package : {packageHead.Result.Title}, priced at {packagePrice.Price}\n");
            sb.Append("For any inquiry you can contact them here on **Unity Developer Hub** by mentioning them in the chat or PM.\n\n");
            sb.Append("*Rating* ");
            for (var i = 0; i < package.Content.Rating.Average; i++)
                sb.Append("★");
            sb.Append($"(:bust_in_silhouette:{package.Content.Rating.Count})\n");
            sb.Append($"Unity Asset Store Link - https://www.assetstore.unity3d.com/en/#!/content/{package.Content.Link.Id}?utm_source=udh&utm_medium=discord\n");
            sb.Append($"```{descStrippedHtml.Substring(0, 250)}[...]```\n");
            sb.Append("To be part of this kind of advertising use `!pInfo` for more informations.");
            //TODO add image

            Stream image;
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                image = await httpClient.GetStreamAsync($"https:{package.Content.Keyimage.Big}");
                //image = ImageSharp.Image.Load(img);
            }

            return (sb.ToString(), image);
        }

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

        public async Task<(bool, string)> VerifyPackage(uint packageId)
        {
            Console.WriteLine("enters verify package");
            var package = await GetPackage(packageId);
            if (package.Content == null) //Package doesn't exist
                return (false, $"The package id {packageId} doesn't exist.");
            if (package.Content.Publisher.SupportEmail.Length < 2)
                return (false, "Your package must have a support email defined to be validated.");

            var name = (await GetPackageHead(packageId)).Result.Publisher;

            Console.WriteLine("before sending verification code");

            await SendVerificationCode(name, package.Content.Publisher.SupportEmail, packageId);
            Console.WriteLine("after sending verification code");
            return (true,
                    "An email with a validation code was sent. Please type !verify *packageId* *code* to validate your package.\nThis code will be valid for 30 minutes."
                );
        }

        public async Task SendVerificationCode(string name, string email, uint packageId)
        {
            var random = new byte[9];
            var rand = RandomNumberGenerator.Create();
            rand.GetBytes(random);

            var code = Convert.ToBase64String(random);

            _verificationCodes[packageId] = code;
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Unity Developer Community", _settings.Email));
            message.To.Add(new MailboxAddress(name, email));
            message.Subject = "Unity Developer Community Package Validation";
            message.Body = new TextPart("plain")
            {
                Text = @"Here's your validation code : " + code
            };

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

        public async Task<string> ValidatePackageWithCode(IUser user, uint packageId, string code)
        {
            string c;
            if (!_verificationCodes.TryGetValue(packageId, out c))
                return "An error occured while trying to veriry your publisher account. Please check your ID is valid.";
            if (c != code)
                return "The verification code is not valid. Please check and try again.";

            var u = (SocketGuildUser)user;
            IRole publisher = u.Guild.GetRole(_settings.PublisherRoleId);
            await u.AddRoleAsync(publisher);

            return "Your publisher account has been verified and you know have the `Asset-Publisher` role!";
        }
    }
}