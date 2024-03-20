using Discord.Commands;
using DiscordBot.Services;
using DiscordBot.Settings;
using DiscordBot.Utils;
using HtmlAgilityPack;

namespace DiscordBot.Modules;

public class GeneralHelpModule : ModuleBase
{
    #region Dependency Injection
    
    public UserService UserService { get; set; }
    public BotSettings BotSettings { get; set; }

    #endregion // Dependency Injection
    
    [Command("error")]
    [Summary("Uses a C# error code, or Unity error code and returns a link to appropriate documentation.")]
    public async Task RespondWithErrorDocumentation(string error)
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        // If we're dealing with C# error
        if (error.StartsWith("CS"))
        {
            // an array of potential url
            List<string> urls = new()
            {
                "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/",
                "https://docs.microsoft.com/en-us/dotnet/csharp/misc/"
            };
            
            HtmlDocument errorPage = null;
            string usedUrl = string.Empty;
            
            foreach (var url in urls)
            {
                errorPage = await WebUtil.GetHtmlDocument($"{url}{error}");
                if (errorPage.DocumentNode.InnerHtml.Contains("Page not found"))
                {
                    continue;
                }
                usedUrl = url;
                break;
            }

            if (errorPage == null)
            {
                await respondFailure(
                    $"Failed to locate {error} error page, however you should try google the error code, there is likely documentation for it.");
                return;
            }

            // We try to pull the first header and pray it contains the error code
            // We grab the first h1 inside the "main" tag, or has class main-column
            string header = errorPage.DocumentNode.SelectSingleNode("//main//h1")?
                .InnerText ?? string.Empty;
            // Attempt to grab the first paragraph inside a class with the id "main"
            string summary = errorPage.DocumentNode.SelectSingleNode("//main//p")?
                .InnerText ?? string.Empty;

            if (string.IsNullOrEmpty(header))
            {
                await respondFailure($"Couldn't find documentation for error code {error}.");
                return;
            }

            // Construct an Embed, Title "C# Error Code: {error}", Description: {summary}, with a link to {url}{error}
            var embed = new EmbedBuilder()
                .WithTitle($"C# Error Code: {error}")
                .WithDescription(summary)
                .WithUrl($"{usedUrl}{error}")
                .FooterRequestedBy(Context.User)
                .Build();

            await ReplyAsync(string.Empty, false, embed);
        }
    }


    private async Task respondFailure(string errorMessage)
    {
        await ReplyAsync(errorMessage).DeleteAfterSeconds(30);
        await Context.Message.DeleteAfterSeconds(30);
    }
}