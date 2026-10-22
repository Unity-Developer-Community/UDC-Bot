using Discord.Net.Rest;

namespace DiscordBot.Services.Recruitment;

/// <summary>Opt-in retry behavior lives only for the enclosing Discord operation, including SDK queue dispatch.</summary>
internal static class RecruitmentRequests
{
    private static readonly DiscordRetryDeadlines SharedDeadlines = new();
    private static readonly AsyncLocal<RequestScope?> Current = new();
    internal static bool IsScoped => Current.Value is not null;

    internal static IDisposable BeginScope(DiscordRetryDeadlines? deadlines = null)
    {
        var scope = new RequestScope(deadlines ?? Current.Value?.Deadlines ?? SharedDeadlines, Current.Value);
        Current.Value = scope;
        return scope;
    }

    private sealed class RequestScope(DiscordRetryDeadlines deadlines, RequestScope? previous) : IDisposable
    {
        public DiscordRetryDeadlines Deadlines { get; } = deadlines;
        public void Dispose() => Current.Value = previous;
    }

    public static RequestOptions Options(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        (Current.Value?.Deadlines ?? SharedDeadlines).CheckGlobal();
        return new RequestOptions { CancelToken = ct, Timeout = 15000, RetryMode = RetryMode.AlwaysFail };
    }

    public static Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken ct, Func<Task<RestResponse>> send) =>
        (Current.Value?.Deadlines ?? SharedDeadlines).SendAsync(method, endpoint, ct, send, IsScoped);
}
