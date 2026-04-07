namespace DiscordBot.Extensions;

public static class EventGuard
{
    public static Func<T, Task> Guarded<T>(Func<T, Task> handler, string name) =>
        async arg =>
        {
            try { await handler(arg); }
            catch (Exception e) { LoggingService.LogToConsole($"[{name}] Unhandled exception: {e}", LogSeverity.Error); }
        };

    public static Func<T1, T2, Task> Guarded<T1, T2>(Func<T1, T2, Task> handler, string name) =>
        async (a1, a2) =>
        {
            try { await handler(a1, a2); }
            catch (Exception e) { LoggingService.LogToConsole($"[{name}] Unhandled exception: {e}", LogSeverity.Error); }
        };

    public static Func<T1, T2, T3, Task> Guarded<T1, T2, T3>(Func<T1, T2, T3, Task> handler, string name) =>
        async (a1, a2, a3) =>
        {
            try { await handler(a1, a2, a3); }
            catch (Exception e) { LoggingService.LogToConsole($"[{name}] Unhandled exception: {e}", LogSeverity.Error); }
        };
}

public static class TaskExtensions
{
    public static void SafeFireAndForget(this Task task, string? caller = null)
    {
        task.ContinueWith(t =>
        {
            if (t.Exception is not { } ex) return;
            if (ex.InnerException is OperationCanceledException) return;

            var prefix = caller != null ? $"[{caller}] " : "";
            LoggingService.LogToConsole($"{prefix}Fire-and-forget exception: {ex}", LogSeverity.Error);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public static Task? DeleteAfterTime(this IDeletable message, int seconds = 0, int minutes = 0, int hours = 0, int days = 0) => message?.DeleteAfterTimeSpan(new TimeSpan(days, hours, minutes, seconds));
    public static Task? DeleteAfterSeconds(this IDeletable message, double seconds) => message?.DeleteAfterTimeSpan(TimeSpan.FromSeconds(seconds));

    public static Task DeleteAfterTimeSpan(this IDeletable message, TimeSpan timeSpan)
    {
        return Task.Delay(timeSpan).ContinueWith(async _ =>
        {
            if (message != null) await message.DeleteAsync();
        });
    }

    public static Task? DeleteAfterTime<T>(this Task<T> task, int seconds = 0, int minutes = 0, int hours = 0, int days = 0, bool awaitDeletion = false) where T : IDeletable => task?.DeleteAfterTimeSpan(new TimeSpan(days, hours, minutes, seconds), awaitDeletion);
    public static Task? DeleteAfterSeconds<T>(this Task<T> task, double seconds, bool awaitDeletion = false) where T : IDeletable => task?.DeleteAfterTimeSpan(TimeSpan.FromSeconds(seconds), awaitDeletion);

    public static Task DeleteAfterTimeSpan<T>(this Task<T> task, TimeSpan timeSpan, bool awaitDeletion = false) where T : IDeletable
    {
        var deletion = Task.Run(async () => await ((await task)?.DeleteAfterTimeSpan(timeSpan) ?? Task.CompletedTask));
        return awaitDeletion ? deletion : task;
    }

    public static Task RemoveAfterSeconds<T>(this ICollection<T> list, T val, double seconds) => list.RemoveAfterTimeSpan(val, TimeSpan.FromSeconds(seconds));

    public static Task RemoveAfterTimeSpan<T>(this ICollection<T> list, T val, TimeSpan timeSpan)
    {
        return Task.Delay(timeSpan).ContinueWith(_ => list.Remove(val));
    }
}