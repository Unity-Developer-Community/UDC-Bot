namespace DiscordBot.Settings;

public class UserSettings
{
    public List<string> Thanks { get; set; } = new List<string> { "thanks", "ty", "thx", "thnx", "thanx", "thankyou", "thank you", "cheers" };
    public int ThanksCooldown { get; set; } = 60;
    public int ThanksMinJoinTime { get; set; } = 600;

    public int XpMinPerMessage { get; set; } = 10;
    public int XpMaxPerMessage { get; set; } = 30;
    public int XpMinCooldown { get; set; } = 60;
    public int XpMaxCooldown { get; set; } = 180;

    public int CodeReminderCooldown { get; set; } = 86400;

    //TODO Introduce notice for asking for help "Can someone help" when they haven't posted in a couple minutes would be a giveaway that they should be reminded to post their question, and not just ask if someone is there.
}