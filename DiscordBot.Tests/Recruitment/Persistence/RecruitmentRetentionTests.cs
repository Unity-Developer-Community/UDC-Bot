using DiscordBot.Services.Recruitment;
using DiscordBot.Services.Recruitment.Domain.Policies;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentRetentionTests
{
    [Theory]
    [InlineData("report", true)]
    [InlineData("material-change", true)]
    [InlineData("publication", true)]
    [InlineData("public-send", true)]
    [InlineData("staff-send", true)]
    [InlineData("attention", true)]
    [InlineData("pin-restoration", true)]
    [InlineData("notice", true)]
    [InlineData("action", false)]
    [InlineData("hold", false)]
    public void TerminalRetentionPreservesExistingPendingWorkPolicy(string work, bool expires)
    {
        var now = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var post = new Listing { Id = "100", Status = ListingStatus.Closed, UpdatedAt = now.AddYears(-2) };
        switch (work)
        {
            case "report": post.Reports.Add(new RecruitmentReport { Concern = "Unresolved concern" }); break;
            case "material-change": post.MaterialChanges = 1; break;
            case "publication": post.PublicationPending = true; break;
            case "public-send": post.PublicSendPending = true; break;
            case "staff-send": post.StaffSendPending = true; break;
            case "attention": post.Attention.Add(new AttentionNotice { SendPending = true }); break;
            case "pin-restoration": post.PublicPin.RestoreArchive = true; break;
            case "notice": post.AcknowledgementNotice.SendPending = true; break;
            case "action": post.PendingAction = new RecruitmentAction(); break;
            case "hold": post.Hold = "Review required"; break;
        }
        var state = new RecruitmentState();
        state.Listings.Add(post.Id, post);
        RecruitmentRetention.Prune(state, now, 12, 24, 30);
        Assert.Equal(!expires, state.Listings.ContainsKey(post.Id));
    }

    [Theory]
    [InlineData("delivery", "Delivery")]
    [InlineData("evidence", "Evidence")]
    public async Task ValidationIdentifiesStateAreaWithoutIncludingPrivateContent(string damage, string area)
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        if (damage == "delivery") post.PublicPin.RestoreLocked = true;
        else post.Reports.Add(new RecruitmentReport { ReporterId = "invalid", Concern = "private content" });
        var forums = rig.State.Forums.ToDictionary(f => f.Key, f => f.Value.ForumId);
        var error = Assert.Throws<InvalidDataException>(() => RecruitmentStateStore.Validate(rig.State, "1", forums));
        Assert.Contains($"Listings[{post.Id}].{area}", error.Message);
        Assert.DoesNotContain("private content", error.Message);
    }
}
