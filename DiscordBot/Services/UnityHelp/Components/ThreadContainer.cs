namespace DiscordBot.Services.UnityHelp;

public class ThreadContainer
{
    public ulong Owner { get; set; }
    public ulong FirstUserMessage { get; set; }
    public ulong ThreadId { get; set; }
    public ulong LatestUserMessage { get; set; }
    public ulong PinnedAnswer { get; set; }

    public bool IsResolved { get; set; } = false;
    public bool HasInteraction { get; set; } = false;


    public ulong BotsLastMessage { get; set; }
    public CancellationTokenSource CancellationToken { get; set; } = null!;
    public DateTime ExpectedShutdownTime { get; set; }

    /// <summary>
    /// Any message the bot sends that could need to be tracked/deleted later is stored here.
    /// </summary>
    public Dictionary<HelpMessageType, HelpBotMessage> HelpMessages { get; set; } = new();

    public bool HasMessage(HelpMessageType type) => HelpMessages.ContainsKey(type);
    public ulong GetMessageId(HelpMessageType type) => HelpMessages[type].MessageId;
    public void AddMessage(HelpMessageType type, ulong messageId) => HelpMessages.Add(type, new HelpBotMessage(messageId, type));
    public void RemoveMessage(HelpMessageType type) => HelpMessages.Remove(type);
}