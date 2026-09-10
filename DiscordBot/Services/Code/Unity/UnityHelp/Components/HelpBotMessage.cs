namespace DiscordBot.Services.Code.Unity.UnityHelp;

public enum HelpMessageType
{
    NoTags,
    QuestionLength,
    AllCapTitle, // Currently toLowers ALL CAP titles
    HelpInTitle, // Currently unused
}

public class HelpBotMessage
{
    public ulong MessageId { get; set; }
    public HelpMessageType Type { get; set; }

    public HelpBotMessage(ulong messageId, HelpMessageType type)
    {
        MessageId = messageId;
        Type = type;
    }
}