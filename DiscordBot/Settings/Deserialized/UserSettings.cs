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
}