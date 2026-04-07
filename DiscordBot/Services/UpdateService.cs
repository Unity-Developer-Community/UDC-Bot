using System.IO;
using System.Text.RegularExpressions;
using Discord.WebSocket;
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
    public FeedData()
    {
        PostedIds = new List<string>();
    }

    public DateTime LastUnityReleaseCheck { get; set; }
    public DateTime LastUnityBlogCheck { get; set; }
    public List<string> PostedIds { get; set; }
}

//TODO Download all avatars to cache them
public class UpdateService
{
    private const string ServiceName = "UpdateService";
    private readonly ILoggingService _loggingService = null!;
    private readonly FeedService _feedService;
    private readonly BotSettings _settings;
    private readonly CancellationToken _token;
    private string[][] _apiDatabase = null!;

    private BotData _botData = null!;
    private readonly DiscordSocketClient _client;
    private List<FaqData> _faqData = null!;
    private FeedData _feedData = null!;

    private string[][] _manualDatabase = null!;
    private UserData _userData = null!;

    public UpdateService(DiscordSocketClient client,
        DatabaseService databaseService, BotSettings settings, FeedService feedService, ILoggingService loggingService,
        CancellationTokenSource cts)
    {
        _client = client;
        _feedService = feedService;
        _loggingService = loggingService as LoggingService;

        _settings = settings;
        _token = cts.Token;

        UpdateLoop();
    }

    private void UpdateLoop()
    {
        ReadDataFromFile();
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

    public List<FaqData> GetFaqData() => _faqData;

    private async Task LoadDocDatabase()
    {
        if (File.Exists($"{_settings.ServerRootPath}/unitymanual.json") &&
            File.Exists($"{_settings.ServerRootPath}/unityapi.json"))
        {
            var json = await File.ReadAllTextAsync($"{_settings.ServerRootPath}/unitymanual.json", _token);
            _manualDatabase = JsonConvert.DeserializeObject<string[][]>(json);
            json = await File.ReadAllTextAsync($"{_settings.ServerRootPath}/unityapi.json", _token);
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

            _manualDatabase = UnityDocParser.ConvertJsToArray(manualInput, true);
            _apiDatabase = UnityDocParser.ConvertJsToArray(apiInput, false);

            if (!SerializeUtil.SerializeFile($"{_settings.ServerRootPath}/unitymanual.json", _manualDatabase))
                await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to save unitymanual.json", ExtendedLogSeverity.Warning);
            if (!SerializeUtil.SerializeFile($"{_settings.ServerRootPath}/unityapi.json", _apiDatabase))
                await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to save unityapi.json", ExtendedLogSeverity.Warning);
        }
        catch (Exception e)
        {
            await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to download manual/api file\nEx:{e.ToString()}", ExtendedLogSeverity.Warning);
        }
    }

    private async Task UpdateDocDatabase()
    {
        try
        {
            while (!_token.IsCancellationRequested)
            {
                if (_botData.LastUnityDocDatabaseUpdate < DateTime.Now - TimeSpan.FromDays(1d))
                    await DownloadDocDatabase();

                await Task.Delay(TimeSpan.FromHours(1), _token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task UpdateRssFeeds()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30d), _token);
            while (!_token.IsCancellationRequested)
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
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    await _loggingService.Log(LogBehaviour.ConsoleChannelAndFile, $"{ServiceName}: Failed to update RSS feeds, attempting to continue.", ExtendedLogSeverity.Error);
                }

                await Task.Delay(TimeSpan.FromSeconds(30d), _token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<(string name, string extract, string url)> DownloadWikipediaArticle(string searchQuery)
    {
        var wikiSearchUri = Uri.EscapeUriString(_settings.WikipediaSearchPage + searchQuery);
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