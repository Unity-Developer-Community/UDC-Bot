namespace CasinoWeb.Hubs;

/// <summary>The messages of the <see cref="Microsoft.AspNetCore.SignalR.HubException"/>s a refused hub call carries. The client matches on them.</summary>
public static class HubErrors
{
    public const string SlowDown = "slow-down";
    public const string CasinoClosed = "casino-closed";
    public const string SessionExpired = "session-expired";
    public const string BadUser = "bad-user";
}
