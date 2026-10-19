using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Policies;
namespace DiscordBot.Services.Recruitment;

public sealed partial class RecruitmentEngine
{
    public async Task ThreadCreatedAsync(ThreadCreation creation, CancellationToken ct)
    {
        if (State is null || !_ready || !State.Forums.Values.Any(f => f.ForumId == creation.ForumId)) return;
        await ThreadChangedAsync(creation.Id, ct);
        if (State.Listings.ContainsKey(creation.Id) || creation.CreatedAt <= Now.AddMinutes(-_settings.AttemptWindowMinutes)) return;
        // THREAD_CREATE can also announce access to an old thread. Only recent, confirmed-absent
        // creations with a known member identity count here; successful discovery already counted itself.
        if (await _discord.GetThreadAsync(creation.Id, ct, refresh: true) is not null) return;
        var author = await _discord.GetAuthorAsync(creation.OwnerId, ct);
        if (author is null || author.IsBot || author.IsStaff) return;
        var post = new Listing
        {
            Id = creation.Id, ForumId = creation.ForumId, OwnerId = creation.OwnerId,
            Title = DiscordDisplayText.Literal(creation.Title), CreatedAt = creation.CreatedAt.ToUniversalTime(), UpdatedAt = Now,
            Status = ListingStatus.Deleted, LastOutcome = "Thread absent before first read; creation observed, deletion actor unknown",
            PublicationPending = true
        };
        State.Listings[post.Id] = post;
        _workflow!.RecordCreation(post); Save();
        await _workflow!.ProcessSafelyAsync(post, ct);
    }

    // Called once per newly retained thread identity, regardless of later deletion or acknowledgement.
}
