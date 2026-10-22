using DiscordBot.Services.Recruitment;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentSubmissionTests
{
    [Theory]
    [InlineData(-1, true, true)]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(-1, false, false)]
    public async Task QueuedSubmissionUsesReceiptTimeBeforeDeadlineActions(int secondsFromDeadline, bool correct, bool accepted)
    {
        using var rig = new RecruitmentRig();
        var service = new RecruitmentService(rig.Settings, rig.Discord, rig.Clock);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await service.StartAsync();
            rig.Discord.Threads["100"] = new ThreadSnapshot("100", "11", "200", "Example", rig.Clock.Now,
                false, false, false, false, Array.Empty<string>(), "Budget AUD 500", rig.Clock.Now.AddYears(-2), rig.Clock.Now.AddMonths(-3));
            service.NotifyThread("100");
            var post = await service.RunAsync((engine, _) => Task.FromResult(engine.State!.Listings["100"]));
            var received = post.Deadline!.Value.AddSeconds(secondsFromDeadline);
            var code = correct ? post.ValidCodes.Last() : "!!!!!";
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocked = service.RunAsync(async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); return true; });
            await entered.Task;
            rig.Clock.Now = received;
            var submission = service.RunAsync((engine, ct) => engine.AcknowledgeAsync("1", "100", "200", code, ct, received), "100");
            rig.Clock.Now = post.Deadline.Value.AddMinutes(1);
            release.SetResult(); await blocked; await submission;
            await service.RunAsync(async (engine, ct) => { await engine.RunDueAsync(ct); return true; });
            Assert.Equal(accepted, post.AcceptedAt.HasValue);
            Assert.Equal(accepted ? ListingStatus.Open : ListingStatus.Deleted, post.Status);
            if (accepted) { Assert.Equal(received, post.AcknowledgedAt); Assert.Empty(rig.Discord.Actions); }
            else Assert.Single(rig.Discord.Actions);
        }
        finally { release.TrySetResult(); await service.StopAsync(default); }
    }
}
