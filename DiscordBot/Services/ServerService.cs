using Discord.WebSocket;

namespace DiscordBot.Services;

public class ServerService
{
    private readonly DiscordSocketClient _client;

    public ServerService(DiscordSocketClient client)
    {
        _client = client;
    }

    public int GetGatewayPing() => _client.Latency;
}
