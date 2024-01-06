namespace DiscordBot.Services;

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
        // Update Database
        await _databaseService.Query().UpdateDefaultCity(user.Id.ToString(), city);
        // Update Cache
        _cityCachedName[user.Id] = city;
        return true;
    }
    
    public async Task<bool> DoesUserHaveDefaultCity(IUser user)
    {
        // Quickest check if we have cached result
        if (_cityCachedName.ContainsKey(user.Id))
            return true;
        
        // Check database
        var res = await _databaseService.Query().GetDefaultCity(user.Id.ToString());
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
        // Update Database
        await _databaseService.Query().UpdateDefaultCity(user.Id.ToString(), null);
        // Update Cache
        _cityCachedName.Remove(user.Id);
        return true;
    }
}