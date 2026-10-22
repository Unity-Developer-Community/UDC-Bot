namespace DiscordBot.Services.Recruitment;

public enum SetupIntent { Reconcile, Repair }
public sealed record SetupRequest(SetupIntent Intent, bool RetryHeld = true, string? Target = null);
