namespace DiscordBot.Services.Recruitment;

/// <summary>Called under the managed work gate. A publication becomes usable only after read-back.</summary>
public sealed class RecruitmentGuidelinePublisher(RecruitmentStateStore store, IRecruitmentPublisher discord,
    RecruitmentGuidelines templates, TimeProvider time)
{
    public async Task<RecruitmentGuidelinePreview> PreviewAsync(RecruitmentForum forum, CancellationToken token)
    {
        RecruitmentForumSetup live = await discord.GetForumAsync(forum.ChannelId, token);
        return new(templates.Render(templates.Load(forum.Kind), "ABCDE"),
            RecruitmentGuidelines.Hash(live.Topic), RecruitmentGuidelines.TagHash(live.Tags));
    }

    public async Task EnsureAsync(RecruitmentForum forum, CancellationToken token,
        string? adoptTopicHash = null, string? repairTagHash = null)
    {
        RecruitmentForumSetup live = await discord.GetForumAsync(forum.ChannelId, token);
        RecruitmentForumPublication publication = (await store.LoadAsync(token))!.Forums[forum.ChannelId].Publication;
        string actualHash = RecruitmentGuidelines.Hash(live.Topic);

        if (adoptTopicHash is not null && actualHash != adoptTopicHash)
        {
            throw new InvalidOperationException("Guidelines changed since preview. Preview again before publishing.");
        }
        if (repairTagHash is not null)
        {
            if (RecruitmentGuidelines.TagHash(live.Tags) != repairTagHash)
            {
                throw new InvalidOperationException("Tags changed since preview. Preview again before repairing.");
            }
            await store.UpdateAsync(state => { state.Forums[forum.ChannelId].Publication.ClosedTagId = null; return true; }, token);
            publication.ClosedTagId = null;
        }

        await EnsureClosedTagAsync(forum.ChannelId, live, publication.ClosedTagId, token);

        // A matching candidate is a successful Discord write whose receipt was not saved.
        if (publication.Candidate is { } candidate && actualHash == candidate.TopicHash)
        {
            await ConfirmAsync(forum.ChannelId, candidate, token);
            publication.Confirmed = candidate;
            publication.Candidate = null;
        }

        string template = templates.Load(forum.Kind);
        string templateHash = RecruitmentGuidelines.Hash(template);
        DateTimeOffset week = RecruitmentGuidelines.WeekStart(time.GetUtcNow());
        bool owned = publication.Confirmed?.TopicHash == actualHash;
        if (adoptTopicHash is null && !owned && (publication.Confirmed is not null || !string.IsNullOrWhiteSpace(live.Topic)))
        {
            throw new InvalidOperationException("Guidelines are unmanaged or changed in Discord; staff preview/adoption is required.");
        }
        if (owned && publication.Confirmed!.TemplateHash == templateHash && publication.Confirmed.WeekStartUtc == week)
        {
            await MarkHealthyAsync(forum.ChannelId, token);
            return;
        }

        candidate = adoptTopicHash is null ? publication.Candidate : null;
        if (candidate is null)
        {
            string code = publication.Confirmed?.WeekStartUtc == week ? publication.Confirmed.Code : RecruitmentGuidelines.NewCode();
            candidate = new()
            {
                Code = code, WeekStartUtc = week, TemplateHash = templateHash,
                TopicHash = RecruitmentGuidelines.Hash(templates.Render(template, code))
            };
            await store.UpdateAsync(state =>
            {
                var saved = state.Forums[forum.ChannelId].Publication;
                saved.Candidate = candidate;
                saved.ExpectedTopicHash = actualHash;
                return true;
            }, token);
        }

        string rendered = templates.Render(template, candidate.Code);
        if (RecruitmentGuidelines.Hash(rendered) != candidate.TopicHash)
        {
            throw new InvalidOperationException("Template changed during publication; preview and explicitly publish again.");
        }
        string expectedHash = (await store.LoadAsync(token))!.Forums[forum.ChannelId].Publication.ExpectedTopicHash!;
        await discord.PublishTopicAsync(forum.ChannelId, expectedHash, rendered, token);
        live = await discord.GetForumAsync(forum.ChannelId, token);
        if (RecruitmentGuidelines.Hash(live.Topic) != candidate.TopicHash)
        {
            throw new InvalidOperationException("Guidelines read-back differs from the candidate; publication is suspended.");
        }
        await ConfirmAsync(forum.ChannelId, candidate, token);
    }

    private async Task EnsureClosedTagAsync(ulong forumId, RecruitmentForumSetup live, ulong? knownId, CancellationToken token)
    {
        RecruitmentForumTag? previous = live.Tags.SingleOrDefault(tag => tag.Id == knownId);
        if (previous is not null && !IsClosed(previous))
        {
            throw new InvalidOperationException("The known Closed tag was renamed; staff preview/tag repair is required.");
        }
        RecruitmentForumTag[] matches = live.Tags.Where(IsClosed).ToArray();
        if (matches.Length > 1 || matches.Any(tag => tag.Moderated))
        {
            throw new InvalidOperationException("Closed must have one unmoderated match; resolve conflicting tags before setup.");
        }
        if (matches.Length == 0)
        {
            if (live.Tags.Count >= 20)
            {
                throw new InvalidOperationException("Forum tag inventory is full; no existing tag will be removed.");
            }
            await discord.AppendClosedTagAsync(forumId, RecruitmentGuidelines.TagHash(live.Tags), token);
            live = await discord.GetForumAsync(forumId, token);
            matches = live.Tags.Where(IsClosed).ToArray();
            if (matches.Length != 1 || matches[0].Moderated)
            {
                throw new InvalidOperationException("Closed tag read-back is ambiguous; setup is suspended.");
            }
        }
        await store.UpdateAsync(state =>
        {
            state.Forums[forumId].Publication.ClosedTagId = matches[0].Id;
            return true;
        }, token);
    }

    private Task ConfirmAsync(ulong forumId, RecruitmentGuidelineReceipt receipt, CancellationToken token) => store.UpdateAsync(state =>
    {
        receipt.PublishedAtUtc = time.GetUtcNow();
        var publication = state.Forums[forumId].Publication;
        publication.Confirmed = receipt;
        publication.Candidate = null;
        publication.ExpectedTopicHash = null;
        publication.Error = null;
        publication.CheckedAtUtc = time.GetUtcNow();
        foreach (var post in state.Posts.Values.Where(post => post.ParentChannelId == forumId &&
                     post.Acknowledgement == RecruitmentAcknowledgement.Pending && post.ChallengeDeadlineUtc > time.GetUtcNow()))
        {
            post.AcceptedCodes = post.AcceptedCodes.Append(receipt.Code).Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(8).ToArray();
        }
        return true;
    }, token);

    private Task MarkHealthyAsync(ulong forumId, CancellationToken token) => store.UpdateAsync(state =>
    {
        state.Forums[forumId].Publication.CheckedAtUtc = time.GetUtcNow();
        state.Forums[forumId].Publication.Error = null;
        return true;
    }, token);

    private static bool IsClosed(RecruitmentForumTag tag) => string.Equals(tag.Name.Trim(), "Closed", StringComparison.OrdinalIgnoreCase);
}
