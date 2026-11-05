using Discord;
using DiscordBot.Services.Server;

namespace DiscordBot.Tests.Services;

public class IntroductionPromptTests
{
    [Fact]
    public void BuiltPrompt_IsRecognised()
    {
        var prompt = IntroductionWatcherService.BuildPrompt(null, null);
        Assert.True(IntroductionWatcherService.IsPromptComponents(prompt.Components));
    }

    [Fact]
    public void OtherContainer_IsNotPrompt()
    {
        var other = new ComponentBuilderV2()
            .WithContainer(new ContainerBuilder().WithTextDisplay("### Welcome to the server"))
            .Build();
        Assert.False(IntroductionWatcherService.IsPromptComponents(other.Components));
    }

    [Fact]
    public void HeadingOutsideContainer_IsNotPrompt()
    {
        var other = new ComponentBuilderV2()
            .WithTextDisplay(IntroductionWatcherService.PromptHeading)
            .Build();
        Assert.False(IntroductionWatcherService.IsPromptComponents(other.Components));
    }

    [Fact]
    public void NoComponents_IsNotPrompt()
    {
        Assert.False(IntroductionWatcherService.IsPromptComponents([]));
    }
}
