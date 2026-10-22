namespace DiscordBot.Services.Recruitment.Domain.Policies;

public sealed record AttemptDecision(IReadOnlyList<DateTimeOffset> Creations, DateTimeOffset? PausedUntil, bool PauseStarted);

public static class AttemptPolicy
{
    public static AttemptDecision Record(IReadOnlyList<DateTimeOffset> creations, DateTimeOffset? pausedUntil,
        DateTimeOffset createdAt, DateTimeOffset now, int windowMinutes, int limit, int pauseMinutes)
    {
        // Additional attempts never extend a pause that is already in progress.
        if (pausedUntil > now) return new(creations, pausedUntil, false);
        var recent = pausedUntil.HasValue ? new List<DateTimeOffset>() : creations.Where(d => d > now.AddMinutes(-windowMinutes)).ToList();
        recent.Add(createdAt);
        var started = recent.Count > limit;
        return new(recent, started ? now.AddMinutes(pauseMinutes) : null, started);
    }
}
