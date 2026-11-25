using System.Net;
using Discord.Net;
using DiscordBot.Services.Moderation;

namespace DiscordBot.Extensions;

public static class CommandCleanupExtensions
{
    /// <summary>
    /// Deletes a member's command message after <paramref name="delay"/>, registering it in <paramref name="registry"/> just
    /// before so the audit log doesn't post a "Message deleted" card for the bot's own cleanup. A message the member
    /// already deleted is ignored (and was logged, since it wasn't registered then).
    /// </summary>
    public static async Task DeleteCommandMessageAsync(this IMessage? message, BotDeletionRegistry registry, TimeSpan delay = default)
    {
        if (message == null)
            return;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay);

        registry.Register([message.Id], DateTimeOffset.UtcNow);
        try
        {
            await message.DeleteAsync();
        }
        catch (HttpException e) when (e.HttpCode == HttpStatusCode.NotFound)
        {
            // Already deleted.
        }
        catch (Exception e) when (delay > TimeSpan.Zero)
        {
            // A delayed clean-up is a side note to the command's reply: don't fail the command over it.
            LoggingService.LogToConsole($"[CommandCleanup] Could not delete command message {message.Id}: {e.Message}", LogSeverity.Warning);
        }
    }
}
