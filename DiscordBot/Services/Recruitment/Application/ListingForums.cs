using System.IO;
using DiscordBot.Settings;
using DiscordBot.Utils;
using DiscordBot.Services.Recruitment.Domain.Analysis;
using DiscordBot.Services.Recruitment.Domain.Policies;
using DiscordBot.Services.Recruitment.Infrastructure.Discord;

namespace DiscordBot.Services.Recruitment.Application;

internal sealed class ListingForums(RecruitmentState state, RecruitmentAssets assets)
{
    private readonly RecruitmentState State = state;
    private readonly RecruitmentAssets _assets = assets;
    public string TagAdvice(ThreadSnapshot live) => _assets.TagAdvice(Purpose(live.ForumId), live.Tags, State.Forums[Purpose(live.ForumId)]);

    public string Purpose(string forum) => State!.Forums.Single(x => x.Value.ForumId == forum).Key;

    public string? ClosingTag(Listing post)
    {
        var purpose = Purpose(post.ForumId);
        return State!.Forums[purpose].Tags.GetValueOrDefault(_assets!.Tags[purpose].Single(t => t.Closing).Key)?.Id;
    }

}
