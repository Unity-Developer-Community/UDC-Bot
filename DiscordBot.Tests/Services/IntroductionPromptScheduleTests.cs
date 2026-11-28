using DiscordBot.Services.Server;

namespace DiscordBot.Tests.Services;

public class IntroductionPromptScheduleTests
{
    private static readonly DateTime Start = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NotDue_BeforeAnyTouch()
    {
        var schedule = new IntroductionPromptSchedule(5);
        Assert.False(schedule.IsDue(Start.AddDays(1)));
        Assert.Null(schedule.DueUtc);
    }

    [Fact]
    public void NotDue_BeforeDelayPasses()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        Assert.False(schedule.IsDue(Start.AddMinutes(5).AddSeconds(-1)));
    }

    [Fact]
    public void Due_OnceDelayHasPassed()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        Assert.True(schedule.IsDue(Start.AddMinutes(5)));
        Assert.True(schedule.IsDue(Start.AddMinutes(6)));
    }

    [Fact]
    public void SecondTouch_PushesDueTimeBack()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        schedule.Touch(Start.AddMinutes(3));
        Assert.False(schedule.IsDue(Start.AddMinutes(5)));
        Assert.True(schedule.IsDue(Start.AddMinutes(8)));
        Assert.Equal(Start.AddMinutes(8), schedule.DueUtc);
    }

    [Fact]
    public void Clear_ResetsSchedule()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        schedule.Clear();
        Assert.False(schedule.IsDue(Start.AddMinutes(10)));
        Assert.Null(schedule.DueUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Delay_ClampedToAtLeastOneMinute(int delayMinutes)
    {
        var schedule = new IntroductionPromptSchedule(delayMinutes);
        Assert.Equal(TimeSpan.FromMinutes(1), schedule.Delay);
    }

    [Fact]
    public void TryTakeDue_ClearsOnlyWhenDue()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        Assert.False(schedule.TryTakeDue(Start.AddMinutes(4)));
        Assert.NotNull(schedule.DueUtc);

        Assert.True(schedule.TryTakeDue(Start.AddMinutes(5)));
        Assert.Null(schedule.DueUtc);
        Assert.False(schedule.TryTakeDue(Start.AddMinutes(6)));
    }

    [Fact]
    public void Retry_AfterTake_SchedulesRetry()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        Assert.True(schedule.TryTakeDue(Start.AddMinutes(5)));

        schedule.Retry(Start.AddMinutes(6));
        Assert.Equal(Start.AddMinutes(6), schedule.DueUtc);
        Assert.True(schedule.TryTakeDue(Start.AddMinutes(6)));
    }

    [Fact]
    public void Retry_DoesNotOverrideNewerTouch()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        Assert.True(schedule.TryTakeDue(Start.AddMinutes(5)));

        // A member posted while the failed prompt was being sent.
        schedule.Touch(Start.AddMinutes(5));
        schedule.Retry(Start.AddMinutes(6));
        Assert.Equal(Start.AddMinutes(10), schedule.DueUtc);
    }

    [Fact]
    public void ScheduleIfIdle_SetsDueWhenNothingScheduled()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.ScheduleIfIdle(Start.AddMinutes(2));
        Assert.Equal(Start.AddMinutes(2), schedule.DueUtc);
    }

    [Fact]
    public void ScheduleIfIdle_KeepsExistingDue()
    {
        var schedule = new IntroductionPromptSchedule(5);
        schedule.Touch(Start);
        schedule.ScheduleIfIdle(Start.AddMinutes(1));
        Assert.Equal(Start.AddMinutes(5), schedule.DueUtc);
    }

    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    [Fact]
    public void DueFromHistory_HumanAfterPrompt_DueDelayAfterTheirMessage()
    {
        Assert.Equal(Start.AddMinutes(4), IntroductionWatcherService.DueFromHistory(Start, Delay, true, Start.AddMinutes(-1)));
    }

    [Fact]
    public void DueFromHistory_OldHumanMessage_DueNow()
    {
        // The case the periodic history read exists for: a message whose event was missed hours ago.
        Assert.Equal(Start, IntroductionWatcherService.DueFromHistory(Start, Delay, true, Start.AddHours(-3)));
    }

    [Fact]
    public void DueFromHistory_PromptIsNewest_NothingDue()
    {
        Assert.Null(IntroductionWatcherService.DueFromHistory(Start, Delay, true, null));
    }

    [Fact]
    public void DueFromHistory_NoPrompt_DueAfterDelay()
    {
        Assert.Equal(Start + Delay, IntroductionWatcherService.DueFromHistory(Start, Delay, false, null));
    }

    [Fact]
    public void DescribeStatus_ShowsStateAndLastProblem()
    {
        var status = new IntroductionWatcherService.WatcherStatus(true, 1, true, 2, 3, Start, Start, Start, null, Start,
            "Failed to post the introduction prompt: timed out", Start);
        var text = IntroductionWatcherService.DescribeStatus(status);
        Assert.Contains("running", text);
        Assert.Contains("https://discord.com/channels/1/2/3", text);
        Assert.Contains("timed out", text);
    }

    [Fact]
    public void DescribeStatus_Disabled()
    {
        var status = new IntroductionWatcherService.WatcherStatus(false, 1, false, null, null, null, null, null, null, null, null, null);
        Assert.Contains("off", IntroductionWatcherService.DescribeStatus(status));
    }
}
