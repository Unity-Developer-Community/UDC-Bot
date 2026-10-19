using DiscordBot.Utils;
using System.Text.RegularExpressions;

namespace DiscordBot.Services.Recruitment;

/// <summary>Offline URL observations. Unknown query identity is scrubbed and never reused for matching.</summary>
public static class RecruitmentPortfolioUrls
{
    public sealed record Identity(string DisplayUrl, string Host, bool Specific, string? ReuseUrl);


    public static Identity? Read(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.UserInfo.Length != 0 || Uri.CheckHostName(host) != UriHostNameType.Dns || !host.Contains('.')) return null;
        var path = uri.AbsolutePath;
        var route = path.Trim('/');
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (DomainName.Matches(host, "discord.com") || DomainName.Matches(host, "discord.gg") || DomainName.Matches(host, "discordapp.com") ||
            DomainName.Matches(host, "t.me") || DomainName.Matches(host, "telegram.me") || DomainName.Matches(host, "wa.me") ||
            parts.Any(p => p.ToLowerInvariant() is "contact" or "login" or "signin" or "invite" or "join")) return null;
        var specific = parts.Length > 0;
        // Selected providers have creator-owned subdomains; their root is already a profile/project.
        if (new[] { "itch.io", "artstation.com", "deviantart.com" }.Any(d => DomainName.Matches(host, d) && host != d && host != "www." + d))
            specific = true;
        // Shared platform entry points, discovery and authentication pages are not work samples.
        if (parts.Length > 0 && parts[0].ToLowerInvariant() is "explore" or "discover" or "search" or "marketplace" or "pricing" or "about" or "features" or "topics" or "trending" or "settings" or "notifications" or "issues" or "pulls" or "dashboard" or "help" or "support" or "feed" or "subscriptions")
            specific = false;
        var builder = new UriBuilder(uri) { Host = host, Fragment = "", Query = "", UserName = "", Password = "" };
        if (builder.Uri.AbsoluteUri.Length > 300) return null;
        var query = ParseQuery(uri.Query);
        if (query is null) return new(builder.Uri.AbsoluteUri, host, false, null);
        var identityQuery = query.Where(q => !Tracking(q.Key)).ToArray();
        string? reuse = null;
        if (DomainName.Matches(host, "youtube.com") || host is "youtu.be" or "www.youtu.be")
        {
            var videos = identityQuery.Where(q => q.Key == "v").ToArray();
            var video = host.EndsWith("youtu.be", StringComparison.Ordinal) && parts.Length == 1 ? parts[0] :
                route == "watch" && videos.Length == 1 ? videos[0].Value :
                parts.Length == 2 && parts[0] is "shorts" or "embed" ? parts[1] : null;
            if (video is not null && Regex.IsMatch(video, "^[A-Za-z0-9_-]{11}$", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
            {
                builder = new UriBuilder("https", "youtube.com", uri.IsDefaultPort ? -1 : uri.Port) { Path = "/watch", Query = "v=" + video };
                specific = true;
                reuse = builder.Uri.AbsoluteUri;
            }
            else
            {
                // /watch with no usable ID, feed pages, and bare domains cannot identify work.
                specific = parts.Length > 0 && (parts[0].StartsWith('@') || parts.Length >= 2 && parts[0] is "channel" or "c" or "user");
                if (specific && identityQuery.Length == 0) reuse = builder.Uri.AbsoluteUri;
            }
        }
        else if (specific && identityQuery.Length == 0) reuse = builder.Uri.AbsoluteUri;
        if (builder.Uri.AbsoluteUri.Length > 300) return null;
        return new(builder.Uri.AbsoluteUri, host, specific, reuse);
    }

    private static bool Tracking(string key) => key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
        key.ToLowerInvariant() is "fbclid" or "gclid" or "dclid" or "msclkid" or "mc_cid" or "mc_eid";

    private static List<KeyValuePair<string, string>>? ParseQuery(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (query.Length > 2000) return null;
        try
        {
            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = pair.Split('=', 2);
                result.Add(new(Uri.UnescapeDataString(fields[0]), fields.Length == 2 ? Uri.UnescapeDataString(fields[1]) : ""));
            }
        }
        catch (UriFormatException) { return null; }
        return result;
    }
}
