namespace DiscordBot.Services.Recruitment.Domain.Policies;

/// <summary>Compatibility interpretation of retained v2 outcome text; not an enforcement input.</summary>
internal static class LegacyListingOutcomes
{
    public static bool IsGuidelineTimeout(Listing post) => post.LastOutcome == "Guidelines acknowledgement timed out";
}
