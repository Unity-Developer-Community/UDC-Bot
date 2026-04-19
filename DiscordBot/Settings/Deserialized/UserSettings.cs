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

    public List<string> Validate()
    {
        var warnings = new List<string>();

        if (XpMinPerMessage > XpMaxPerMessage)
            warnings.Add($"XpMinPerMessage ({XpMinPerMessage}) > XpMaxPerMessage ({XpMaxPerMessage})");
        if (XpMinCooldown > XpMaxCooldown)
            warnings.Add($"XpMinCooldown ({XpMinCooldown}) > XpMaxCooldown ({XpMaxCooldown})");
        if (ThanksCooldown <= 0)
            warnings.Add($"ThanksCooldown is {ThanksCooldown} — should be positive");
        if (Thanks.Count == 0)
            warnings.Add("Thanks list is empty — thanks/karma feature will never trigger");

        return warnings;
    }
}