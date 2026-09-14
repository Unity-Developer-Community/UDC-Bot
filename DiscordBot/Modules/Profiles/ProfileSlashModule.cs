using Discord.Interactions;
using Discord.WebSocket;

namespace DiscordBot.Modules.Profiles;

public class ProfileSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    public ProfileCardService ProfileCardService { get; set; } = null!;
    public ILoggingService LoggingService { get; set; } = null!;

    [SlashCommand("profile", "Display your profile card, or another user's")]
    public async Task DisplayProfile(
        [Summary("user", "The user whose profile card you want to see (defaults to you)")] IUser? user = null)
    {
        await Context.Interaction.DeferAsync();

        await SendProfileCardAsync(user ?? Context.User);
    }

    [UserCommand("View Profile")]
    public async Task ViewProfileContext(IUser user)
    {
        await Context.Interaction.DeferAsync(ephemeral: true);

        await SendProfileCardAsync(user);
    }

    [SlashCommand("join-date", "Show when you, or another member, joined the server")]
    public async Task JoinDate(
        [Summary("user", "The user whose join date you want to see (defaults to you)")] IUser? user = null)
    {
        var target = user ?? Context.User;
        if (target is not SocketGuildUser guildUser || guildUser.JoinedAt is not { } joinedAt)
        {
            await Context.Interaction.RespondAsync("❌ Could not determine the join date for that user.",
                ephemeral: true);
            return;
        }

        await Context.Interaction.RespondAsync($"{guildUser.Mention} joined **{joinedAt:dddd dd/MM/yyyy HH:mm:ss}**");
    }

    [SlashCommand("karma", "Explain what karma is")]
    public async Task Karma()
    {
        await Context.Interaction.RespondAsync(
            "Karma is tracked on your `/profile` which helps indicate how much you've helped others " +
            "and provides a small increase in EXP gain.");
    }

    private async Task SendProfileCardAsync(IUser user)
    {
        try
        {
            var profileCardPath = await ProfileCardService.GenerateProfileCard(user);
            if (string.IsNullOrEmpty(profileCardPath))
            {
                var failure = await Context.Interaction.FollowupAsync("❌ Failed to generate the profile card.");
                await (failure.DeleteAfterTime(seconds: 10) ?? Task.CompletedTask);
                return;
            }

            // Matches the legacy !profile behaviour, which cleared the card after 3 minutes to limit clutter.
            var card = await Context.Interaction.FollowupWithFileAsync(profileCardPath);
            await (card.DeleteAfterTime(minutes: 3) ?? Task.CompletedTask);
        }
        catch (Exception e)
        {
            await LoggingService.LogAction($"Error while generating profile card for {user.Username}.\nEx:{e}",
                ExtendedLogSeverity.LowWarning);
        }
    }
}
