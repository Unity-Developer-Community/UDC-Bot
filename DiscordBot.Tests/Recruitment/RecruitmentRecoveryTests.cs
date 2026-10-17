using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Modules.Recruitment;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentRecoveryTests
{
    [Fact]
    public async Task InteractionModuleRegistersWithoutConnectingOrStartingTheFeature()
    {
        using var client = new DiscordSocketClient();
        using var interactions = new InteractionService(client);
        var service = new RecruitmentService(client, new BotSettings());
        using var provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var module = await interactions.AddModuleAsync<RecruitmentModule>(provider);
        Assert.Equal(9, module.SlashCommands.Count);
        Assert.Contains(module.SlashCommands, c => c.Name == "portfolio-sites");
        Assert.Contains(module.SlashCommands, c => c.Name == "status");
        Assert.Contains(module.SlashCommands, c => c.Name == "eligibility");
        Assert.DoesNotContain(module.SlashCommands, c => c.Name == "help");
        Assert.Equal(7, module.ComponentCommands.Count);
        Assert.Equal(2, module.ModalCommands.Count);
    }

    [Fact]
    public async Task LostCloseResponseIsConfirmedAfterRestartWithoutRepeatingAction()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack();
        rig.Discord.LoseNextCloseResponse = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Staff("close"));
        await rig.Restart();
        Assert.Equal(ListingStatus.Closed, rig.State.Listings["100"].Status);
        Assert.Single(rig.Discord.Actions);
        Assert.Null(rig.State.Listings["100"].PendingAction);
    }

    [Fact]
    public async Task MissingStarterReservesCapacityAndGetsFullWindowAfterRecovery()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var starter = new ThreadSnapshot("100", "11", "200", "Delayed starter", rig.Clock.Now, false, false, false, false,
            Array.Empty<string>(), null, null, null);
        rig.Discord.Threads["100"] = starter; await rig.Sweep();
        Assert.Null(rig.State.Listings["100"].Deadline);
        var newer = await rig.Post("101", forum: "14"); await rig.Ack("101"); Assert.Null(newer.AcceptedAt);
        rig.Clock.Advance(TimeSpan.FromHours(2));
        rig.Discord.Threads["100"] = starter with { Starter = "Now available" }; await rig.Sweep();
        Assert.Equal(rig.Clock.Now.AddMinutes(30), rig.State.Listings["100"].Deadline);
    }

    [Fact]
    public async Task FirstEnableDiscoversPostsCreatedBeforeBotStartup()
    {
        using var rig = new RecruitmentRig();
        rig.Discord.Threads["100"] = new ThreadSnapshot("100", "11", "200", "Before startup", rig.Clock.Now.AddDays(-2), false, false, false, false,
            Array.Empty<string>(), "A listing", null, null);
        await rig.Start();
        Assert.Equal(rig.Clock.Now.AddMinutes(30), rig.State.Listings["100"].Deadline);
        Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task RecreatedPromptGetsFreshUsableWindow()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        rig.Clock.Advance(TimeSpan.FromMinutes(31)); rig.Discord.Messages.Remove("100:recruitment:100:public");
        await rig.Sweep();
        Assert.Equal(rig.Clock.Now.AddMinutes(30), rig.State.Listings["100"].Deadline);
        Assert.Empty(rig.Discord.Actions);
    }

    [Fact]
    public async Task FourthTimeoutDoesNotSendAnotherThresholdAlert()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        for (var i = 0; i < 4; i++) { await rig.Post((100 + i).ToString()); rig.Clock.Advance(TimeSpan.FromMinutes(31)); await rig.Sweep(); }
        Assert.Contains("Moderator attention", rig.Discord.Messages["99:recruitment:102:staff"].Message.Content);
        Assert.DoesNotContain("Moderator attention", rig.Discord.Messages["99:recruitment:103:staff"].Message.Content);
    }

    [Fact]
    public async Task StoreWriteFailureFencesLaterActionsAndPreservesBackup()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        var path = Path.Combine(rig.Root, "recruitment/recruitment-state.json");
        var backup = File.ReadAllText(path + ".bak"); File.WriteAllText(path, "broken");
        rig.Clock.Advance(TimeSpan.FromHours(1)); await rig.Sweep();
        Assert.Empty(rig.Discord.Actions); Assert.Contains("state write failure", rig.Engine.Status);
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
        Assert.Contains("unavailable", await rig.Engine.CloseOwnerAsync("1", "100", "200", default));
    }

    [Fact]
    public async Task TimelyQueuedAcknowledgementWinsAgainstTimeoutEvenIfWorkerRunsLate()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        var received = post.Deadline!.Value.AddSeconds(-1);
        rig.Clock.Now = post.Deadline.Value.AddMinutes(1);
        rig.PendingThreadWork.Add(post.Id);
        var result = await rig.Engine.AcknowledgeAsync("1", post.Id, post.OwnerId, post.ValidCodes.Last(), default, received);
        Assert.Contains("accepted", result); Assert.Empty(rig.Discord.Actions);
        Assert.NotNull(post.AcceptedAt);
    }

    [Fact]
    public async Task LateSubmissionDoesNotBypassDeadlineEvenWhenTimeoutIsQueued()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        rig.Clock.Now = post.Deadline!.Value.AddMinutes(1); rig.PendingThreadWork.Add(post.Id);
        Assert.Contains("expired", await rig.Ack()); Assert.Null(post.AcceptedAt);
        rig.PendingThreadWork.Clear(); await rig.Sweep();
        Assert.Equal(ListingStatus.Deleted, post.Status);
    }

    [Fact]
    public async Task MissingThreadClearsUnfinishedPublicDeliveryAndDoesNotResetTerminalRetention()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        post.PublicSendPending = true; rig.Discord.Threads.Remove(post.Id); await rig.Sweep();
        var deletedAt = post.UpdatedAt;
        Assert.False(post.PublicSendPending); Assert.False(post.PublicationPending);
        rig.Clock.Advance(TimeSpan.FromDays(2)); await rig.Sweep();
        Assert.Equal(deletedAt, post.UpdatedAt);
    }

    [Fact]
    public async Task StaffPreviewRedactsRotatingAnswerAndIncludesFullGuidelines()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var preview = await rig.Staff("preview", "paid-recruiting");
        Assert.Contains("One open listing", preview); Assert.Contains("Managed tags", preview);
        Assert.DoesNotContain(rig.State.RotationCode, preview);
    }

    [Fact]
    public async Task StaffReopenCannotGrantSecondSlot()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Ack(); await rig.Staff("close");
        rig.Clock.Advance(TimeSpan.FromDays(31)); await rig.Post("101"); await rig.Ack("101");
        Assert.Contains("Another listing", await rig.Staff("reopen"));
        Assert.Null(rig.State.Listings["100"].PendingAction);
        Assert.Equal(ListingStatus.Closed, rig.State.Listings["100"].Status);
        Assert.Equal(ListingStatus.Open, rig.State.Listings["101"].Status);
    }
}
