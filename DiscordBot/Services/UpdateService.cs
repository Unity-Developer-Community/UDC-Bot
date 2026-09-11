using System.IO;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using DiscordBot.Components;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using DiscordBot.Utils;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public class BotData
{
    public DateTime LastUnityDocDatabaseUpdate { get; set; }
}

public class UserData
{
    public UserData()
    {
        MutedUsers = new Dictionary<ulong, DateTime>();
        CodeReminderCooldown = new Dictionary<ulong, DateTime>();
    }

    public Dictionary<ulong, DateTime> MutedUsers { get; set; }
    public Dictionary<ulong, DateTime> CodeReminderCooldown { get; set; }
}

public class FeedData
{
    public FeedData()
    {
        PostedIds = new List<string>();
    }

    public DateTime LastUnityReleaseCheck { get; set; }
    public DateTime LastUnityBlogCheck { get; set; }
    public List<string> PostedIds { get; set; }
}

//TODO Download all avatars to cache them
public class UpdateService : IManagedBotService, IComponentHealthContributor
{
    private const string ServiceName = "UpdateService";
    private readonly ILoggingService _loggingService;
    private readonly FeedService _feedService;
    private readonly string _serverRootPath;
    private readonly ulong _guildId;
    private readonly ulong _mutedRoleId;
    private readonly string _wikipediaSearchPage;
    private readonly bool _feedsConfigured;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _lifecycleCancellation;
    private Task? _lifetimeTask;
    private string[][] _apiDatabase;

    private BotData _botData;
    private readonly DiscordSocketClient _client;
    private FeedData _feedData;

    private string[][] _manualDatabase;
    private UserData _userData;

    public string ComponentId => ComponentIds.Updates;
    public bool IsRunning => _lifetimeTask is { IsCompleted: false };

    public UpdateService(DiscordSocketClient client,
        FeedService feedService,
        ILoggingService loggingService,
        IOptions<StorageOptions> storageOptions,
        IOptions<DiscordGuildOptions> guildOptions,
        IOptions<ModerationOptions> moderationOptions,
        IOptions<KnowledgeSearchOptions> knowledgeSearchOptions,
        FeatureConfigurationCatalog featureConfiguration)
    {
        _client = client;
        _feedService = feedService;
        _loggingService = loggingService;
        _serverRootPath = storageOptions.Value.ServerRootPath;
        _guildId = guildOptions.Value.GuildId;
        _mutedRoleId = moderationOptions.Value.MutedRoleId;
        _wikipediaSearchPage = knowledgeSearchOptions.Value.WikipediaSearchPage;
        _feedsConfigured = featureConfiguration.Get(ComponentIds.Feeds).IsConfigured;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                return;

            ReadDataFromFile();
            _lifecycleCancellation?.Dispose();
            _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _lifecycleCancellation.Token;
            _lifetimeTask = Task.WhenAll(
                SaveDataToFile(token),
                UpdateDocDatabase(token),
                _feedsConfigured ? UpdateRssFeeds(token) : Task.CompletedTask,
                RestoreMutedUsers(token));
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_lifetimeTask is null)
                return;
            await _lifecycleCancellation!.CancelAsync();
            try
            {
                await _lifetimeTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
            {
            }

            await SaveDataOnceAsync();
            _lifetimeTask = null;
            _lifecycleCancellation.Dispose();
            _lifecycleCancellation = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task<ComponentHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ComponentHealthSnapshot(
            IsRunning ? ComponentRuntimeState.Running : ComponentRuntimeState.Stopped,
            IsRunning
                ? _feedsConfigured
                    ? "Persistence, documentation, and feed workers are running."
                    : "Persistence and documentation workers are running; feeds are unavailable."
                : "Update workers are stopped.",
            DateTimeOffset.UtcNow));

    private void ReadDataFromFile()
    {
        _botData = SerializeUtil.DeserializeFile<BotData>($"{_serverRootPath}/botdata.json");

        _userData = SerializeUtil.DeserializeFile<UserData>($"{_serverRootPath}/userdata.json");
        _feedData = SerializeUtil.DeserializeFile<FeedData>($"{_serverRootPath}/feeds.json");
    }

    private async Task RestoreMutedUsers(CancellationToken cancellationToken)
    {
        while (_client.ConnectionState != ConnectionState.Connected ||
               _client.LoginState != LoginState.LoggedIn)
            await Task.Delay(100, cancellationToken);

        await Task.Delay(10000, cancellationToken);
        var removalTasks = new List<Task>();
        foreach (var userId in _userData.MutedUsers)
        {
            if (!_userData.MutedUsers.HasUser(userId.Key, true))
                continue;

            var guild = _client.Guilds.First(g => g.Id == _guildId);
            var socketUser = guild.GetUser(userId.Key);
            if (socketUser == null)
                continue;

            IGuildUser user = socketUser;
            var mutedRole = user.Guild.GetRole(_mutedRoleId);
            if (!user.RoleIds.Contains(_mutedRoleId))
                await user.AddRoleAsync(mutedRole);
            removalTasks.Add(RemoveMuteWhenDue(user, mutedRole, cancellationToken));
        }

        await Task.WhenAll(removalTasks);
    }

    private async Task RemoveMuteWhenDue(
        IGuildUser user,
        IRole mutedRole,
        CancellationToken cancellationToken)
    {
        var remaining = _userData.MutedUsers[user.Id] - DateTime.Now;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken);
        await user.RemoveRoleAsync(mutedRole);
    }

    // Saves data to file
    private async Task SaveDataToFile(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await SaveDataOnceAsync();
            await Task.Delay(TimeSpan.FromSeconds(20d), cancellationToken);
        }
    }

    private async Task SaveDataOnceAsync()
    {
        await SerializeUtil.SerializeFileAsync($"{_serverRootPath}/botdata.json", _botData);
        await SerializeUtil.SerializeFileAsync($"{_serverRootPath}/userdata.json", _userData);
        await SerializeUtil.SerializeFileAsync($"{_serverRootPath}/feeds.json", _feedData);
    }

    public async Task<string[][]> GetManualDatabase()
    {
        if (_manualDatabase == null)
            await LoadDocDatabase();
        return _manualDatabase;
    }

    public async Task<string[][]> GetApiDatabase()
    {
        if (_apiDatabase == null)
            await LoadDocDatabase();
        return _apiDatabase;
    }

    private async Task LoadDocDatabase()
    {
        if (File.Exists($"{_serverRootPath}/unitymanual.json") &&
            File.Exists($"{_serverRootPath}/unityapi.json"))
        {
            var json = await File.ReadAllTextAsync($"{_serverRootPath}/unitymanual.json", CurrentToken);
            _manualDatabase = JsonConvert.DeserializeObject<string[][]>(json);
            json = await File.ReadAllTextAsync($"{_serverRootPath}/unityapi.json", CurrentToken);
            _apiDatabase = JsonConvert.DeserializeObject<string[][]>(json);
        }
        else
            await DownloadDocDatabase();
    }

    private async Task DownloadDocDatabase()
    {
        try
        {
            var htmlWeb = new HtmlWeb();
            htmlWeb.CaptureRedirect = true;

            var manual = await htmlWeb.LoadFromWebAsync("https://docs.unity3d.com/Manual/docdata/index.js");
            var manualInput = manual.DocumentNode.OuterHtml;

            var api = await htmlWeb.LoadFromWebAsync("https://docs.unity3d.com/ScriptReference/docdata/index.js");
            var apiInput = api.DocumentNode.OuterHtml;

            _manualDatabase = ConvertJsToArray(manualInput, true);
            _apiDatabase = ConvertJsToArray(apiInput, false);

            if (!SerializeUtil.SerializeFile($"{_serverRootPath}/unitymanual.json", _manualDatabase))
                await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to save unitymanual.json", ExtendedLogSeverity.Warning);
            if (!SerializeUtil.SerializeFile($"{_serverRootPath}/unityapi.json", _apiDatabase))
                await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to save unityapi.json", ExtendedLogSeverity.Warning);

            string[][] ConvertJsToArray(string data, bool isManual)
            {
                var list = new List<string[]>();
                string pagesInput;
                if (isManual)
                {
                    pagesInput = data.Split("info = [")[0].Split("pages=")[1];
                    pagesInput = pagesInput.Substring(2, pagesInput.Length - 4);
                }
                else
                {
                    pagesInput = data.Split("info =")[0];
                    pagesInput = pagesInput.Substring(63, pagesInput.Length - 65);
                }

                foreach (var s in pagesInput.Split("],["))
                {
                    var ps = s.Split(",");
                    list.Add(new[] { ps[0].Replace("\"", ""), ps[1].Replace("\"", "") });
                    //Console.WriteLine(ps[0].Replace("\"", "") + "," + ps[1].Replace("\"", ""));
                }

                return list.ToArray();
            }
        }
        catch (Exception e)
        {
            await _loggingService.LogException(e, $"{ServiceName}: Failed to download manual/api file", LogBehaviour.ConsoleChannelAndFile, ExtendedLogSeverity.Warning);
        }
    }

    private async Task UpdateDocDatabase(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_botData.LastUnityDocDatabaseUpdate < DateTime.Now - TimeSpan.FromDays(1d))
                await DownloadDocDatabase();

            await Task.Delay(TimeSpan.FromHours(1), cancellationToken);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private async Task UpdateRssFeeds(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30d), cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_feedData != null)
                {
                    if (_feedData.LastUnityReleaseCheck < DateTime.Now - TimeSpan.FromMinutes(5))
                    {
                        _feedData.LastUnityReleaseCheck = DateTime.Now;

                        await _feedService.CheckUnityBetasAsync(_feedData);
                        await _feedService.CheckUnityReleasesAsync(_feedData);
                    }

                    if (_feedData.LastUnityBlogCheck < DateTime.Now - TimeSpan.FromMinutes(10))
                    {
                        _feedData.LastUnityBlogCheck = DateTime.Now;

                        await _feedService.CheckUnityBlogAsync(_feedData);
                    }
                }
            }
            catch (Exception e)
            {
                await _loggingService.LogException(e, $"{ServiceName}: Failed to update RSS feeds, attempting to continue.", LogBehaviour.ConsoleChannelAndFile);
            }

            await Task.Delay(TimeSpan.FromSeconds(30d), cancellationToken);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    public async Task<(string name, string extract, string url)> DownloadWikipediaArticle(string searchQuery)
    {
        var wikiSearchUri = Uri.EscapeUriString(_wikipediaSearchPage + searchQuery);
        var htmlWeb = new HtmlWeb { CaptureRedirect = true };
        HtmlDocument wikiSearchResponse;

        try
        {
            wikiSearchResponse = await htmlWeb.LoadFromWebAsync(wikiSearchUri, CurrentToken);
        }
        catch (Exception exception)
        {
            await _loggingService.LogException(exception, $"{ServiceName}: Wikipedia request failed", LogBehaviour.ConsoleChannelAndFile, ExtendedLogSeverity.Warning);
            return (null, null, null);
        }

        try
        {
            var job = JObject.Parse(wikiSearchResponse.Text);

            if (job.TryGetValue("query", out var query))
            {
                var pages = JsonConvert.DeserializeObject<List<WikiPage>>(job[query.Path]["pages"].ToString(), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                if (pages != null && pages.Count > 0)
                {
                    pages.Sort((x, y) => x.Index.CompareTo(y.Index)); //Sort from smallest index to biggest, smallest index is indicitive of best matching result
                    var page = pages[0];

                    const string referToString = "may refer to:...";
                    var referToIndex = page.Extract.IndexOf(referToString, StringComparison.Ordinal);
                    //If a multi-refer result was given, reformat title to indicate this and strip the "may refer to" portion from the body
                    if (referToIndex > 0)
                    {
                        var splitIndex = referToIndex + referToString.Length;
                        page.Title = page.Extract.Substring(0, splitIndex - 4); //-4 to strip the useless characters since this will be a title
                        page.Extract = page.Extract.Substring(splitIndex);
                        page.Extract = page.Extract.Replace("\n", Environment.NewLine + "-");
                    }
                    else
                        page.Extract = page.Extract.Replace("\n", Environment.NewLine);

                    //TODO Not a perfect solution. ``!wiki Quaternion`` and a few other formula pages due to formatting will result a mess without this marked by "displaystyle" currently, so we just sanitize the text if we see that.
                    // This will also help shrink embeds, but it removes paragraphs as well, making it a wall of text.
                    if (page.Extract.Contains("displaystyle"))
                        page.Extract = Regex.Replace(page.Extract, @"\s+", " ");

                    return (page.Title + ":", page.Extract, page.FullUrl.ToString());
                }
            }
            else
                return (null, null, null);
        }
        catch (Exception e)
        {
            await _loggingService.LogException(e, $"{ServiceName}: Wikipedia response parsing failed", LogBehaviour.ConsoleChannelAndFile, ExtendedLogSeverity.Warning);
        }

        return (null, null, null);
    }

    public UserData GetUserData() => _userData;

    public void SetUserData(UserData data)
    {
        _userData = data;
    }

    private CancellationToken CurrentToken => _lifecycleCancellation?.Token ?? CancellationToken.None;

    /// <summary>
    ///     JSON object for the Wikipedia command to convert results to.
    /// </summary>
    private class WikiPage
    {
        [JsonProperty("index")]
        public long Index { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("extract")]
        public string Extract { get; set; }

        [JsonProperty("fullurl")]
        public Uri FullUrl { get; set; }
    }
}
