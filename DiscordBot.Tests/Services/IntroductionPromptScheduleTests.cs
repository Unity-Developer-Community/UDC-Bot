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
}
