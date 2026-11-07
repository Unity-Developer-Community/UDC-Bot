using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using DiscordBot.Domain;
using DiscordBot.Domain.UnityNews;
using DiscordBot.Settings;
using DiscordBot.Utils;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Services;

public class BotData
{
    public DateTime LastUnityDocDatabaseUpdate { get; set; }
}

public class UserData
{
    public UserData()
    {
        CodeReminderCooldown = new Dictionary<ulong, DateTime>();
    }

    public Dictionary<ulong, DateTime> CodeReminderCooldown { get; set; }
}

public class FaqData
{
    public string Question { get; set; } = null!;
    public string Answer { get; set; } = null!;
    public string[] Keywords { get; set; } = null!;
}

public class FeedData
{
    /// <summary>The newest ids kept in <see cref="PostedIds"/>; blog feeds list far fewer items than this.</summary>
    public const int MaxPostedIds = 200;

    public FeedData()
    {
        PostedIds = new List<string>();
        ReleaseStreams = new Dictionary<string, ReleaseStreamState>();
    }

    public DateTime LastUnityBlogCheck { get; set; }
    public List<string> PostedIds { get; set; }

    /// <summary>True once the blog feed's existing posts were recorded as posted without posting them (the first check of a new state).</summary>
    public bool BlogSeeded { get; set; }

    /// <summary>When the Unity release streams were last checked (UTC).</summary>
    public DateTimeOffset LastUnityReleaseCheckUtc { get; set; }

    /// <summary>Release post state per stream, keyed by <see cref="UnityStream"/> name.</summary>
    public Dictionary<string, ReleaseStreamState> ReleaseStreams { get; set; }

    /// <summary>Remembers an id as posted and drops the oldest ones beyond <see cref="MaxPostedIds"/>.</summary>
    public void AddPostedId(string id)
    {
        PostedIds.Add(id);
        if (PostedIds.Count > MaxPostedIds)
            PostedIds.RemoveRange(0, PostedIds.Count - MaxPostedIds);
    }
}

//TODO Download all avatars to cache them
public class UpdateService
{
    private const string ServiceName = "UpdateService";
    private const string ManualIndexUrl = "https://docs.unity3d.com/Manual/docdata/index.js";
    private const string ApiIndexUrl = "https://docs.unity3d.com/ScriptReference/docdata/index.js";
    private static readonly TimeSpan DocDatabaseMaxAge = TimeSpan.FromDays(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggingService _loggingService = null!;
    private readonly FeedService _feedService;
    private readonly BotSettings _settings;
    private readonly CancellationToken _token;
    private DocEntry[] _apiDatabase = null!;

    private BotData _botData = null!;
    private List<FaqData> _faqData = null!;
    private FeedData _feedData = null!;

    private DocEntry[] _manualDatabase = null!;
    private UserData _userData = null!;

    public UpdateService(DatabaseService databaseService, BotSettings settings, FeedService feedService, ILoggingService loggingService,
        CancellationTokenSource cts, IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _feedService = feedService;
        _loggingService = (loggingService as LoggingService)!;

        _settings = settings;
        _token = cts.Token;

        UpdateLoop();
    }

    private static readonly TimeSpan FirstFeedCheckDelay = TimeSpan.FromSeconds(30);

    private void UpdateLoop()
    {
        ReadDataFromFile();
        if (_feedData != null)
            _loggingService.LogAction($"[{nameof(FeedService)}] " + FeedService.StartupLine(_feedData,
                _settings.Channels.UnityReleases?.Id ?? 0, _settings.Channels.UnityNews?.Id ?? 0,
                DateTimeOffset.UtcNow + FirstFeedCheckDelay), ExtendedLogSeverity.Info);
        Task.Run(SaveDataToFile, _token);
        // Task.Run(UpdateUserRanks, _token);
        Task.Run(UpdateDocDatabase, _token);
        Task.Run(UpdateRssFeeds, _token);
    }

    private void ReadDataFromFile()
    {
        _botData = SerializeUtil.DeserializeFile<BotData>($"{_settings.ServerRootPath}/botdata.json");

        _userData = SerializeUtil.DeserializeFile<UserData>($"{_settings.ServerRootPath}/userdata.json");

        _faqData = SerializeUtil.DeserializeFile<List<FaqData>>("Settings/FAQs.json");
        _feedData = SerializeUtil.DeserializeFile<FeedData>($"{_settings.ServerRootPath}/feeds.json");
    }

    // Saves data to file
    private async Task SaveDataToFile()
    {
        try
        {
            while (!_token.IsCancellationRequested)
            {
                await SerializeUtil.SerializeFileAsync($"{_settings.ServerRootPath}/botdata.json", _botData);
                await SerializeUtil.SerializeFileAsync($"{_settings.ServerRootPath}/userdata.json", _userData);
                await SerializeUtil.SerializeFileAsync($"{_settings.ServerRootPath}/feeds.json", _feedData);
                await Task.Delay(TimeSpan.FromSeconds(20d), _token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<DocEntry[]?> GetManualDatabase()
    {
        if (_manualDatabase == null)
            await LoadDocDatabase();
        return _manualDatabase;
    }

    public async Task<DocEntry[]?> GetApiDatabase()
    {
        if (_apiDatabase == null)
            await LoadDocDatabase();
        return _apiDatabase;
    }

    public List<FaqData> GetFaqData() => _faqData;

    private string ManualPath => $"{_settings.ServerRootPath}/unitymanual.json";
    private string ApiPath => $"{_settings.ServerRootPath}/unityapi.json";

    private async Task LoadDocDatabase()
    {
        if (File.Exists(ManualPath) && File.Exists(ApiPath))
        {
            _manualDatabase = await ReadDocFile(ManualPath);
            _apiDatabase = await ReadDocFile(ApiPath);
        }
        else
            await DownloadDocDatabase();
    }

    /// <summary>Reads a doc file without first loading it into one large string.</summary>
    private static async Task<DocEntry[]> ReadDocFile(string path)
    {
        await using var file = File.OpenRead(path);
        using var reader = new JsonTextReader(new StreamReader(file));
        return await Task.Run(() => JsonSerializer.CreateDefault().Deserialize<DocEntry[]>(reader)!);
    }

    private async Task DownloadDocDatabase()
    {
        try
        {
            var manual = UnityDocParser.ConvertJsToArray(await DownloadDocIndex(ManualIndexUrl), true);
            var api = UnityDocParser.ConvertJsToArray(await DownloadDocIndex(ApiIndexUrl), false);
            _manualDatabase = manual;
            _apiDatabase = api;
            _botData.LastUnityDocDatabaseUpdate = DateTime.Now;

            if (!SerializeUtil.SerializeFile(ManualPath, _manualDatabase))
                await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to save unitymanual.json", ExtendedLogSeverity.Warning);
            if (!SerializeUtil.SerializeFile(ApiPath, _apiDatabase))
                await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to save unityapi.json", ExtendedLogSeverity.Warning);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to download manual/api file\nEx:{e.ToString()}", ExtendedLogSeverity.Warning);
        }
    }

    /// <summary>The page list of a doc index. Streams the response and stops at <see cref="UnityDocParser.InfoMarker"/>, so the rest is never held in memory.</summary>
    private async Task<string> DownloadDocIndex(string url)
    {
        using var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(_token);
        using var reader = new StreamReader(stream);
        return await UnityDocParser.ReadUntilAsync(reader, UnityDocParser.InfoMarker, _token);
    }

    private async Task UpdateDocDatabase()
    {
        try
        {
            while (!_token.IsCancellationRequested)
            {
                var lastUpdate = LastDocDatabaseUpdate(_botData.LastUnityDocDatabaseUpdate, ManualPath, ApiPath);
                if (lastUpdate < DateTime.Now - DocDatabaseMaxAge)
                    await DownloadDocDatabase();

                await Task.Delay(TimeSpan.FromHours(1), _token);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// When the doc files were last downloaded. Older versions never saved the time, so an unset time
    /// falls back to when the files were written, and an upgrade doesn't download them again at once.
    /// </summary>
    internal static DateTime LastDocDatabaseUpdate(DateTime recorded, string manualPath, string apiPath)
    {
        if (recorded != default)
            return recorded;
        if (!File.Exists(manualPath) || !File.Exists(apiPath))
            return default;
        var manualWritten = File.GetLastWriteTime(manualPath);
        var apiWritten = File.GetLastWriteTime(apiPath);
        return manualWritten < apiWritten ? manualWritten : apiWritten;
    }

    private async Task UpdateRssFeeds()
    {
        try
        {
            await Task.Delay(FirstFeedCheckDelay, _token);
            while (!_token.IsCancellationRequested)
            {
                try
                {
                    if (_feedData != null)
                    {
                        if (_feedData.LastUnityReleaseCheckUtc < DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5))
                        {
                            _feedData.LastUnityReleaseCheckUtc = DateTimeOffset.UtcNow;

                            await _feedService.CheckUnityReleasesAsync(_feedData);
                        }

                        if (_feedData.LastUnityBlogCheck < DateTime.Now - TimeSpan.FromMinutes(10))
                        {
                            _feedData.LastUnityBlogCheck = DateTime.Now;

                            await _feedService.CheckUnityBlogAsync(_feedData);
                        }
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to update RSS feeds, attempting to continue.", ExtendedLogSeverity.Error);
                }

                await Task.Delay(TimeSpan.FromSeconds(30d), _token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<(string? name, string? extract, string? url)> DownloadWikipediaArticle(string searchQuery)
    {
        var wikiSearchUri = _settings.WikipediaSearchPage + Uri.EscapeDataString(searchQuery);
        var htmlWeb = new HtmlWeb { CaptureRedirect = true };
        HtmlDocument wikiSearchResponse;

        try
        {
            wikiSearchResponse = await htmlWeb.LoadFromWebAsync(wikiSearchUri, _token);
        }
        catch
        {
            await _loggingService.LogChannelAndFile($"{ServiceName}: Wikipedia method failed loading URL: {wikiSearchUri}", ExtendedLogSeverity.Warning);
            return (null, null, null);
        }

        try
        {
            var job = JObject.Parse(wikiSearchResponse.Text);

            if (job.TryGetValue("query", out var query))
            {
                var pagesToken = job[query.Path]?["pages"];
                if (pagesToken == null) return (null, null, null);
                var pages = JsonConvert.DeserializeObject<List<WikiPage>>(pagesToken.ToString(), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

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
            await _loggingService.LogChannelAndFile($"{ServiceName}: Wikipedia method likely failed to parse JSON response from: {wikiSearchUri}.\nEx:{e.ToString()}", ExtendedLogSeverity.Warning);
        }

        return (null, null, null);
    }

    public UserData GetUserData() => _userData;

    public void SetUserData(UserData data)
    {
        _userData = data;
    }

    /// <summary>
    ///     JSON object for the Wikipedia command to convert results to.
    /// </summary>
    private class WikiPage
    {
        [JsonProperty("index")]
        public long Index { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; } = null!;

        [JsonProperty("extract")]
        public string Extract { get; set; } = null!;

        [JsonProperty("fullurl")]
        public Uri FullUrl { get; set; } = null!;
    }
}