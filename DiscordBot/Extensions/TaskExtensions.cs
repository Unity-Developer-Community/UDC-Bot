namespace DiscordBot.Extensions;

public static class TaskExtensions
{
    public static Task DeleteAfterTime(this IDeletable message, int seconds = 0, int minutes = 0, int hours = 0, int days = 0) => message?.DeleteAfterTimeSpan(new TimeSpan(days, hours, minutes, seconds));
    public static Task DeleteAfterSeconds(this IDeletable message, double seconds) => message?.DeleteAfterTimeSpan(TimeSpan.FromSeconds(seconds));

    public static Task DeleteAfterTimeSpan(this IDeletable message, TimeSpan timeSpan)
    {
        return Task.Delay(timeSpan).ContinueWith(async _ =>
        {
            if (message != null) await message?.DeleteAsync();
        });
    }

    public static Task DeleteAfterTime<T>(this Task<T> task, int seconds = 0, int minutes = 0, int hours = 0, int days = 0, bool awaitDeletion = false) where T : IDeletable => task?.DeleteAfterTimeSpan(new TimeSpan(days, hours, minutes, seconds), awaitDeletion);
    public static Task DeleteAfterSeconds<T>(this Task<T> task, double seconds, bool awaitDeletion = false) where T : IDeletable => task?.DeleteAfterTimeSpan(TimeSpan.FromSeconds(seconds), awaitDeletion);

    public static Task DeleteAfterTimeSpan<T>(this Task<T> task, TimeSpan timeSpan, bool awaitDeletion = false) where T : IDeletable
    {
        var deletion = Task.Run(async () => await (await task)?.DeleteAfterTimeSpan(timeSpan));
        return awaitDeletion ? deletion : task;
    }

    public static Task RemoveAfterSeconds<T>(this ICollection<T> list, T val, double seconds) => list.RemoveAfterTimeSpan(val, TimeSpan.FromSeconds(seconds));

    public static Task RemoveAfterTimeSpan<T>(this ICollection<T> list, T val, TimeSpan timeSpan)
    {
        return Task.Delay(timeSpan).ContinueWith(_ => list.Remove(val));
    }
}