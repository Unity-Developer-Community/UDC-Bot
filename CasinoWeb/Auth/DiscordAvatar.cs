namespace CasinoWeb.Auth;

public static class DiscordAvatar
{
    public static string Url(string userId, string? avatarHash)
    {
        if (!string.IsNullOrEmpty(avatarHash))
            return $"https://cdn.discordapp.com/avatars/{userId}/{avatarHash}.png?size=128";

        // Accounts without an avatar get one of six default images, picked from the id.
        var index = ulong.TryParse(userId, out var id) ? (id >> 22) % 6 : 0;
        return $"https://cdn.discordapp.com/embed/avatars/{index}.png";
    }
}
