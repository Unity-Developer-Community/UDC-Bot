using Discord.WebSocket;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Hosting;

public interface IDiscordGateway
{
    event Func<Task> Ready;

    Task LoginAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class DiscordGateway(
    DiscordSocketClient client,
    IOptions<DiscordConnectionOptions> connectionOptions) : IDiscordGateway
{
    public event Func<Task> Ready
    {
        add => client.Ready += value;
        remove => client.Ready -= value;
    }

    public async Task LoginAsync(CancellationToken cancellationToken) =>
        await client.LoginAsync(TokenType.Bot, connectionOptions.Value.Token).WaitAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken) =>
        await client.StartAsync().WaitAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync().WaitAsync(cancellationToken);
        await client.LogoutAsync().WaitAsync(cancellationToken);
    }
}
