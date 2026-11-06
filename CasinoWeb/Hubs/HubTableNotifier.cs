using CasinoWeb.Tables;
using Microsoft.AspNetCore.SignalR;

namespace CasinoWeb.Hubs;

/// <summary>
/// Pushes what changed at the tables to the browsers. Each seated user and watcher gets a view built for them on the server
/// and sent to their own connections only (<c>Clients.User</c>), so hidden cards never reach anyone else.
/// </summary>
public sealed class HubTableNotifier(IHubContext<CasinoHub> hub) : ITableNotifier
{
    public Task TablesChangedAsync(IReadOnlyList<TableSummary> tables) =>
        hub.Clients.All.SendAsync("TablesChanged", tables.Select(TableSummaryDto.From).ToArray());

    public async Task TableUpdatedAsync(int tableId, IReadOnlyDictionary<ulong, TableView?> views)
    {
        // One after the other: the pushes of a table must reach a client in the order the changes happened.
        foreach (var (userId, view) in views)
        {
            await hub.Clients.User(userId.ToString()).SendAsync("TableUpdated", new TableUpdateDto(tableId, view is null ? null : TableViewDto.From(view)));
        }
    }
}
