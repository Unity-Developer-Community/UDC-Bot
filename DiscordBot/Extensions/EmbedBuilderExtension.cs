namespace DiscordBot.Extensions;

public static class EmbedBuilderExtension
{
    
    public static EmbedBuilder FooterRequestedBy(this EmbedBuilder builder, IUser requestor)
    {
        builder.WithFooter(
            $"Requested by {requestor.GetUserPreferredName()}", 
            requestor.GetAvatarUrl());
        return builder;
    }
    
    public static EmbedBuilder FooterQuoteBy(this EmbedBuilder builder, IUser requestor, IChannel channel)
    {
        builder.WithFooter(
            $"Quoted by {requestor.GetUserPreferredName()}, • From channel #{channel.Name}", 
            requestor.GetAvatarUrl());
        return builder;
    }
    
    public static EmbedBuilder FooterInChannel(this EmbedBuilder builder, IChannel channel)
    {
        builder.WithFooter(
            $"In channel #{channel.Name}", null);
        return builder;
    }
    
    public static EmbedBuilder AddAuthor(this EmbedBuilder builder, IUser user, bool includeAvatar = true)
    {
        builder.WithAuthor(
            user.GetUserPreferredName(),
            includeAvatar ? user.GetAvatarUrl() : null);
        return builder;
    }
    
    public static EmbedBuilder AddAuthorWithAction(this EmbedBuilder builder, IUser user, string action, bool includeAvatar = true)
    {
        builder.WithAuthor(
            $"{user.GetUserPreferredName()} - {action}",
            includeAvatar ? user.GetAvatarUrl() : null);
        return builder;
    }
    
}