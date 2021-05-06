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
        private readonly DatabaseService _databaseService;

        private readonly Settings.Deserialized.Settings _settings;

        private Dictionary<uint, string> _verificationCodes;

        public PublisherService(DiscordSocketClient client, DatabaseService databaseService, Settings.Deserialized.Settings settings)
        {
            _client = client;
            _databaseService = databaseService;
            _verificationCodes = new Dictionary<uint, string>();
            _settings = settings;
        }

        public async Task PostAd(uint id)
        {
            (uint, ulong) ad = _databaseService.GetPublisherAd(id);
            await PublisherAdvertising(ad.Item1, ad.Item2);
        }

        public async Task PublisherAdvertising(uint packageId, ulong userid)
        {
            Console.WriteLine("pub1 " + packageId);
            PackageObject package = await GetPackage(packageId);
            PackageHeadObject packageHead = await GetPackageHead(packageId);
            PriceObject packagePrice = await GetPackagePrice(packageId);
            Console.WriteLine("pub2");
            (string, Stream) r = await GetPublisherAdvertisting(userid, package, packageHead, packagePrice);
            Console.WriteLine("pub3");

            var channel = _client.GetChannel(_settings.UnityNewsChannel.Id) as ISocketMessageChannel;
            await channel.SendFileAsync(r.Item2, "image.jpg", r.Item1);
        }

        /*
        DailyObject => https://www.assetstore.unity3d.com/api/en-US/sale/results/10.json
        PackageOBject => https://www.assetstore.unity3d.com/api/en-US/content/overview/[PACKAGEID].json
        PriceObject (€) => https://www.assetstore.unity3d.com/api/en-US/content/price/[PACKAGEID].json
        PackageHead => https://www.assetstore.unity3d.com/api/en-US/head/package/[PACKAGEID].json
        blogfeed (xml) => https://blogs.unity3d.com/feed/
        */

        public async Task<DailyObject> GetDaily()
        {
            using (var httpClient = new HttpClient())
            {
                string json = await httpClient.GetStringAsync(
                    $"https://www.assetstore.unity3d.com/api/en-US/sale/results/10.json");
                return JsonConvert.DeserializeObject<DailyObject>(json);
            }
        }

        public async Task<PackageObject> GetPackage(uint packageId)
        {
            using (var httpClient = new HttpClient())
            {
                string json = await httpClient.GetStringAsync(
                    $"https://www.assetstore.unity3d.com/api/en-US/content/overview/{packageId}.json");
                return JsonConvert.DeserializeObject<PackageObject>(json);
            }
        }

        public async Task<PackageHeadObject> GetPackageHead(uint packageId)
        {
            using (var httpClient = new HttpClient())
            {
                string json = await httpClient.GetStringAsync(
                    $"https://www.assetstore.unity3d.com/api/en-US/head/package/{packageId}.json");
                return JsonConvert.DeserializeObject<PackageHeadObject>(json);
            }
        }

        public async Task<PriceObject> GetPackagePrice(uint packageId)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                string json = await httpClient.GetStringAsync(
                    $"https://www.assetstore.unity3d.com/api/en-US/content/price/{packageId}.json");
                return JsonConvert.DeserializeObject<PriceObject>(json);
            }
        }

        public async Task<(string, Stream)> GetPublisherAdvertisting(ulong userid, PackageObject package,
            PackageHeadObject packageHead, PriceObject packagePrice)
        {
            string descStrippedHtml = Regex.Replace(package.content.description, "<.*?>", String.Empty);
            descStrippedHtml = Regex.Replace(descStrippedHtml, "&nbsp;", String.Empty);

            StringBuilder sb = new StringBuilder();
            sb.Append("**--- Publisher everyday Advertising ---**\n\n");
            sb.Append($"Today's daily advertisting goes to {_client.GetUser(userid).Mention} (**{packageHead.result.publisher}**)\n");
            sb.Append($"With their package : {packageHead.result.title}, priced at {packagePrice.price}\n");
            sb.Append("For any inquiry you can contact them here on **Unity Developer Hub** by mentioning them in the chat or PM.\n\n");
            sb.Append("*Rating* ");
            for (int i = 0; i < package.content.rating.average; i++)
                sb.Append("★");
            sb.Append($"(:bust_in_silhouette:{package.content.rating.count})\n");
            sb.Append($"Unity Asset Store Link - https://www.assetstore.unity3d.com/en/#!/content/{package.content.link.id}?utm_source=udh&utm_medium=discord\n");
            sb.Append($"```{descStrippedHtml.Substring(0, 250)}[...]```\n");
            sb.Append("To be part of this kind of advertising use `!pInfo` for more informations.");
            //TODO: add image

            Stream image;
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                image = await httpClient.GetStreamAsync($"https:{package.content.keyimage.big}");
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
                string content = await webClient.DownloadStringTaskAsync($"https://assetstore.unity.com/publishers/{ publisherId }");
                if (!content.Contains("Error 404"))
                {
                    string email = "";
                    Match emailMatch = new Regex("mailto:([^\"]+)").Match(content);
                    if (emailMatch.Success)
                        email = emailMatch.Groups[1].Value;

                    if (email.Length > 2)
                    {
                        // No easy way to take their name, so we pass their discord name in.
                        await SendVerificationCode(name, email, publisherId);
                        return (true, "An email with a validation code was sent. Please type !verify *publisherID* *code* to validate your package.\nThis code will be valid for 30 minutes.");
                    }
                }
                return (false, "We failed to confirm this Publisher ID, double check and try again in a few minutes.");
            }
        }


        public async Task<(bool, string)> VerifyPackage(uint packageId)
        {
            Console.WriteLine("enters verify package");
            PackageObject package = await GetPackage(packageId);
            if (package.content == null) //Package doesn't exist
                return (false, $"The package id {packageId} doesn't exist.");
            if (package.content.publisher.support_email.Length < 2)
                return (false, "Your package must have a support email defined to be validated.");

            string name = (await GetPackageHead(packageId)).result.publisher;

            Console.WriteLine("before sending verification code");

            await SendVerificationCode(name, package.content.publisher.support_email, packageId);
            Console.WriteLine("after sending verification code");
            return (true,
                "An email with a validation code was sent. Please type !verify *packageId* *code* to validate your package.\nThis code will be valid for 30 minutes."
                );
        }

        public async Task SendVerificationCode(string name, string email, uint packageId)
        {
            Console.WriteLine("mail");
            byte[] random = new byte[9];
            RandomNumberGenerator rand = RandomNumberGenerator.Create();
            rand.GetBytes(random);

            string code = Convert.ToBase64String(random);

            _verificationCodes[packageId] = code;
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Unity Developer Community", _settings.Gmail));
            message.To.Add(new MailboxAddress(name, email));
            message.Subject = "Unity Developer Community Package Validation";
            message.Body = new TextPart("plain")
            {
                Text = @"Here's your validation code : " + code
            };

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync("smtp.gmail.com", 587);

                client.AuthenticationMechanisms.Remove("XOAUTH2");
                await client.AuthenticateAsync(_settings.GmailUsername, _settings.GmailPassword);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }

            //TODO: Delete code after 30min
        }

        public async Task<string> ValidatePackageWithCode(IUser user, uint packageId, string code)
        {
            string c;
            if (!_verificationCodes.TryGetValue(packageId, out c))
                return "An error occured while trying to validate your package. Please verify your packageId is valid";
            if (c != code)
                return "The verification code is not valid. Please verify it and try again.";

            var u = user as SocketGuildUser;
            IRole publisher = u.Guild.GetRole(_settings.PublisherRoleId);
            await u.AddRoleAsync(publisher);
            await _databaseService.AddPublisherPackage(user.Username, user.DiscriminatorValue.ToString(), user.Id.ToString(), packageId);

            return "Your package has been verified and added to the daily advertisement list.";
        }
    }
}