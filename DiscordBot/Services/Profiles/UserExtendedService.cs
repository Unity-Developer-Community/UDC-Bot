namespace DiscordBot.Services.Profiles;

/// <summary>
/// May be renamed later.
/// Current purpose is a cache for user data for "fun" commands which only includes DefaultCity behaviour.
/// </summary>
public class UserExtendedService
{
    private readonly DatabaseService _databaseService;

    // Cached Information
    private Dictionary<ulong, string> _cityCachedName = new();

    public UserExtendedService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<bool> SetUserDefaultCity(IUser user, string city)
    {
        var query = _databaseService.Query;
        if (query is null) return false;
        // Update Database
        await query.UpdateDefaultCity(user.Id.ToString(), city);
        // Update Cache
        _cityCachedName[user.Id] = city;
        return true;
    }

    public async Task<bool> DoesUserHaveDefaultCity(IUser user)
    {
        // Quickest check if we have cached result
        if (_cityCachedName.ContainsKey(user.Id))
            return true;

        var query = _databaseService.Query;
        if (query is null) return false;
        // Check database
        var res = await query.GetDefaultCity(user.Id.ToString());
        if (string.IsNullOrEmpty(res))
            return false;

        // Cache result
        _cityCachedName[user.Id] = res;
        return true;
    }

    public async Task<string> GetUserDefaultCity(IUser user)
    {
        if (await DoesUserHaveDefaultCity(user))
            return _cityCachedName[user.Id];
        return "";
    }

    public async Task<bool> RemoveUserDefaultCity(IUser user)
    {
        var query = _databaseService.Query;
        if (query is null) return false;
        // Update Database
        await query.UpdateDefaultCity(user.Id.ToString(), null);
        // Update Cache
        _cityCachedName.Remove(user.Id);
        return true;
    }
}