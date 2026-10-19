using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Presentation;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingQueries(RecruitmentState state, RecruitmentSettings settings, IRecruitmentDiscord discord, TimeProvider time, ListingOwnerCommands owners, Func<bool> evidenceCurrent)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentSettings _settings = settings;
    private readonly IRecruitmentDiscord _discord = discord;
    private DateTimeOffset Now => time.GetUtcNow();

    public async Task<Listing?> ReaderListingAsync(string guild, string thread, string reader, CancellationToken ct) =>
        State is not null && guild == State.GuildId && State.Listings.TryGetValue(thread, out var post) &&
        post.Status is not ListingStatus.Reference && await _discord.IsMemberAsync(guild, reader, ct) ? post : null;

    public async Task<string> EligibilityAsync(string guild, string owner, CancellationToken ct)
    {
        if (State is null || guild != State.GuildId || !await _discord.IsMemberAsync(guild, owner, ct))
            return "This control is only available to current members of the configured server.";
        State.Authors.TryGetValue(owner, out var author);
        var open = State.Listings.Values.Where(p => p.OwnerId == owner && p.Status == ListingStatus.Open).Take(10)
            .Select(p => new EligibilityListing(p.Id, p.AcceptedAt.HasValue)).ToArray();
        var used = author is null || author.AttemptPausedUntil.HasValue && author.AttemptPausedUntil <= Now
            ? 0 : author.RecentCreations.Count(d => d > Now.AddMinutes(-_settings.AttemptWindowMinutes));
        var view = new EligibilityView(open, author is not null, author?.LastAcceptedCreation?.AddDays(_settings.CooldownDays),
            author?.AttemptPausedUntil, Now, evidenceCurrent(), used, _settings.CooldownDays, _settings.AttemptLimit,
            _settings.AttemptWindowMinutes, _settings.AttemptPauseMinutes);
        return RecruitmentQueryFormatter.Eligibility(view);
    }

    public async Task<string> DetailsAsync(string guild, string thread, string reader, CancellationToken ct)
    {
        var post = await ReaderListingAsync(guild, thread, reader, ct);
        return post is null ? "Listing details are unavailable in this server." : RecruitmentPresentation.Details(post);
    }

    public async Task<(string Message, bool Open, bool Removable)> OwnerDetailsAsync(string guild, string thread, string owner, CancellationToken ct)
    {
        var post = owners.Owner(guild, thread, owner);
        if (post is null) return ("This listing does not belong to you or recruitment is unavailable.", false, false);
        return ($"**Your listing · {post.Status}**\n" + await EligibilityAsync(guild, owner, ct) +
            "\nClosing preserves the discussion. Removal permanently deletes it.", post.Status == ListingStatus.Open,
            post.Status is ListingStatus.Open or ListingStatus.Closed);
    }

    public string EvidenceDetails(Listing post)
    {
        var versions = new[] { post.OriginalStarter }.Concat(post.EarlierMaterialVersions).Append(post.LatestStarter)
            .OfType<StarterVersion>().DistinctBy(v => (v.ObservedAt, v.Fingerprint)).ToArray();
        RelatedListing[] related = Array.Empty<RelatedListing>();
        if (post.LatestStarter is { } latest)
        {
            static IEnumerable<StarterVersion> Retained(Listing p) => new[] { p.OriginalStarter }.Concat(p.EarlierMaterialVersions)
                .Append(p.LatestStarter).OfType<StarterVersion>();
            var links = Retained(post).SelectMany(v => v.Links).ToHashSet(StringComparer.Ordinal);
            related = State!.Listings.Values.Where(p => p.Id != post.Id && p.OwnerId != post.OwnerId &&
                Retained(p).Any(other => latest.Content.Length >= 120 && latest.Fingerprint == other.Fingerprint || links.Overlaps(other.Links)))
                .OrderByDescending(p => p.CreatedAt).Take(5).Select(p => new RelatedListing(p.Id, p.OwnerId)).ToArray();
        }
        return RecruitmentQueryFormatter.Evidence(new PrivateEvidenceView(RecruitmentPresentation.Staff(State, post, _settings).Content,
            post.CreatedAt, post.AcceptedAt, post.PublicPin, versions, related, post.Reports.ToArray(), post.Audit.TakeLast(10).ToArray()));
    }

}
