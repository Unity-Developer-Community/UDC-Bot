using System.Text.RegularExpressions;

namespace DiscordBot.Domain.UnityNews;

/// <summary>
/// Which blog posts get a card in the discussion channel. Every post still goes to the news channel; this keeps out the
/// ones that rarely start a conversation there: AI tooling and the CLI, and monetization or advertising pitches.
/// </summary>
public static class BlogDiscussionFilter
{
    /// <summary>Topics and tags from the article page (case-insensitive, whole label) that keep a post out.</summary>
    public static readonly IReadOnlyList<string> DefaultBlockedTopics =
        ["AI", "AI features", "CLI", "Monetization", "User acquisition", "Advertising", "Ads"];

    /// <summary>
    /// Words or phrases in the title (case-insensitive, whole words) that keep a post out. Only the title: descriptions
    /// mention AI for game AI and beginner posts too. Needed because some pages have no topics, and a feed-only card has no page.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultBlockedTitleWords =
    [
        "AI", "vibe code", "vibe coding", "CLI", "LLM", "agentic",
        "monetization", "monetize", "advertising", "advertisers", "ads", "ad revenue", "user acquisition", "marketing", "marketers",
    ];

    /// <summary>
    /// Why the post is kept out of the discussion channel (for the log), or null when it gets a card. <paramref name="topics"/>
    /// and <paramref name="tags"/> come from the article page and are empty when it couldn't be read.
    /// </summary>
    public static string? ExcludedBy(string title, IEnumerable<string> topics, IEnumerable<string> tags,
        IReadOnlyList<string> blockedTopics, IReadOnlyList<string> blockedTitleWords)
    {
        foreach (var label in topics.Concat(tags))
        {
            var blocked = blockedTopics.FirstOrDefault(b => string.Equals(b.Trim(), label.Trim(), StringComparison.OrdinalIgnoreCase));
            if (blocked != null)
                return $"topic or tag \"{label.Trim()}\"";
        }

        foreach (var word in blockedTitleWords.Where(w => !string.IsNullOrWhiteSpace(w)))
        {
            // Whole words: "ads" must not match "loads", "AI" must not match "said".
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word.Trim())}(?![\p{{L}}\p{{N}}])";
            if (Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return $"\"{word.Trim()}\" in the title";
        }
        return null;
    }
}
