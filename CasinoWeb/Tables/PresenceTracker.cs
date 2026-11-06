namespace CasinoWeb.Tables;

/// <summary>
/// Counts each user's open connections. SignalR's reconnect makes a new connection, so a user is present while at least one is
/// open; only the first connection and the last disconnection are news for a table.
/// </summary>
public sealed class PresenceTracker
{
    private readonly Dictionary<ulong, int> _connections = [];

    /// <returns><c>true</c> when this is the user's first open connection.</returns>
    public bool Connected(ulong userId)
    {
        lock (_connections)
        {
            var count = _connections.GetValueOrDefault(userId) + 1;
            _connections[userId] = count;
            return count == 1;
        }
    }

    /// <returns><c>true</c> when that was the user's last open connection.</returns>
    public bool Disconnected(ulong userId)
    {
        lock (_connections)
        {
            if (!_connections.TryGetValue(userId, out var count))
            {
                return false;
            }

            if (count > 1)
            {
                _connections[userId] = count - 1;
                return false;
            }

            _connections.Remove(userId);
            return true;
        }
    }
}
