using Discord.Commands;
using DiscordBot.Services;

namespace DiscordBot.Modules;

[Group("UserModule"), Alias("")]
public class ProfileModule : ModuleBase
{
    public ProfileCardService ProfileCardService { get; set; }
    public ILoggingService LoggingService { get; set; }

    [Command("Profile"), Priority(2)]
    [Summary("Display your profile card.")]
    public async Task DisplayProfile()
    {
        await DisplayProfile(Context.Message.Author);
    }

    [Command("Profile"), Priority(2)]
    [Summary("Display profile card of mentioned user. Syntax : !profile @user")]
    public async Task DisplayProfile(IUser user)
    {
        try
        {
            await Context.Message.DeleteAsync();

            var profileCard = await ProfileCardService.GenerateProfileCard(user);
            if (string.IsNullOrEmpty(profileCard))
            {
                await ReplyAsync("Failed to generate profile card.").DeleteAfterSeconds(seconds: 10);
                return;
            }

            var profile = await Context.Channel.SendFileAsync(profileCard);
            await profile.DeleteAfterTime(minutes: 3);
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error while generating profile card for {user.Username}.\nEx:{e}",
                ExtendedLogSeverity.LowWarning);
        }
    }
}
