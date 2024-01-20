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
    
    public static EmbedBuilder AddAuthor(this EmbedBuilder builder, IUser user, bool includeAvatar = true)
    {
        builder.WithAuthor(
            user.GetUserPreferredName(),
            includeAvatar ? user.GetAvatarUrl() : null);
        return builder;
    }
    
    
}