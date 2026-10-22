using System.IO;
using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Presentation;

internal static class ListingEligibilityText
{

    public static string EligibilityReason(Eligibility eligibility)
    {
        if (eligibility.AttemptPausedUntil is { } pause) return $"Posting attempts paused until {pause:yyyy-MM-dd HH:mm} UTC. Check /recruitment eligibility.";
        if (eligibility.BlockingListingId is { } id) return $"Existing listing {id} occupies your slot. Update or bump that thread; check /recruitment eligibility.";
        return $"Accepted-listing cooldown ends {eligibility.NextAllowedAt:yyyy-MM-dd HH:mm} UTC. Check /recruitment eligibility.";
    }

}
