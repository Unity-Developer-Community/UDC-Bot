namespace DiscordBot.Extensions;

public static class MessageExtensions
{
    public static async Task<bool> TrySendMessage(this IDMChannel channel, string message = "", Embed embed = null)
    {
        try
        {
            await channel.SendMessageAsync(message, embed: embed);
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }
}