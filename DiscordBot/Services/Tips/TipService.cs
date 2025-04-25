using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.Services.Tips.Components;
using DiscordBot.Settings;
using Newtonsoft.Json;

namespace DiscordBot.Services.Tips;

public class TipService
{
    private const string ServiceName = "TipService"; 
    private const string DatabaseName = "tips.json";

    private readonly BotSettings _settings;
    private readonly ILoggingService _loggingService;
    private readonly string _imageDirectory;

    private ConcurrentDictionary<string, List<Tip>> _tips = new();
    private bool _isRunning = false;
    private bool _readOnly = false;

    private Regex keywordPattern = null;

    public TipService(BotSettings settings, ILoggingService loggingService)
    {
        _settings = settings;
        _loggingService = loggingService;

        if (string.IsNullOrEmpty(_settings.ServerRootPath))
        {
            _loggingService.LogAction($"[{ServiceName}] ServerRootPath not set, service will not run.", ExtendedLogSeverity.Warning);
            _isRunning = false;
            return;
        }
        
        if (string.IsNullOrEmpty(_settings.TipImageDirectory))
        {
            _loggingService.LogAction($"[{ServiceName}] TipImageDirectory not set, service will not run.", ExtendedLogSeverity.Warning);
            _isRunning = false;
            return;
        }

        _imageDirectory = Path.Combine(_settings.ServerRootPath, _settings.TipImageDirectory);

        Initialize();
    }
    
    private void Initialize()
    {
        if (_isRunning) return;

        _readOnly = false;
        var jsonPath = GetTipPath(DatabaseName);;
        if (!Directory.Exists(_imageDirectory))
        {
            _loggingService.LogAction($"[{ServiceName}] Tip directory {_imageDirectory} did not exist.", ExtendedLogSeverity.Info);
            Directory.CreateDirectory(_imageDirectory);
            File.WriteAllText(jsonPath, "{}");
        }
        else
        {
            var directorySize = new DirectoryInfo(_imageDirectory).EnumerateFiles("*.*", SearchOption.AllDirectories).Sum(file => file.Length);
            if (directorySize > _settings.TipMaxDirectoryFileSize)
            {
                _loggingService.LogAction($"[{ServiceName}] Tip directory size is {directorySize / 1024 / 1024f:.#} MB, exceeding the limit of {_settings.TipMaxDirectoryFileSize / 1024 / 1024f:.#} MB, no additional content will be added during this session.", ExtendedLogSeverity.Warning);
                _readOnly = true;
            }
            else
            {
                _loggingService.LogAction($"[{ServiceName}] Tip directory size is {directorySize / 1024 / 1024f:.#} MB, within the limit of {_settings.TipMaxDirectoryFileSize / 1024 / 1024f:.#} MB.", ExtendedLogSeverity.Info);
                _loggingService.LogAction($"[{ServiceName}] Tip directory contains {new DirectoryInfo(_imageDirectory).EnumerateFiles("*.*", SearchOption.AllDirectories).Count()} files.",
                    ExtendedLogSeverity.Info);
            }
            
            if (File.Exists(jsonPath))
            {
                var json =  File.ReadAllText(jsonPath);
                _tips = JsonConvert.DeserializeObject<ConcurrentDictionary<string, List<Tip>>>(json);
                _loggingService.LogAction(
                    $"[{ServiceName}] Tip index has {_tips.Count} keywords.",
                    ExtendedLogSeverity.Info);
            }
        }

        _isRunning = true;
    }

    private bool IsValidTipKeyword(string keyword)
    {
        // Start with ascii letter
        // continue with ascii letters, digits, limited punctuation
        // no whitespace, no commas
        //
        // valid examples:  "dr.mendeleev" "f451" "wash_hands" "Poe's-Law"
        //
        if (keywordPattern == null)
            keywordPattern = new Regex(@"^[a-z][a-z.0-9_'-]*$", RegexOptions.IgnoreCase);

        if (!keywordPattern.IsMatch(keyword))
            return false;

        return true;
    }

    private bool IsValidTipAttachment(IAttachment attachment)
    {
        if (attachment.Size > _settings.TipMaxImageFileSize)
            return false;

        // Discord-friendly attachment image file formats only
        //
        if (attachment.Filename.EndsWith(".png")) return true;
        if (attachment.Filename.EndsWith(".webp")) return true;
        if (attachment.Filename.EndsWith(".jpg")) return true;

        return false;
    }

    public string GetTipPath(string filename)
    {
        return Path.Combine(_imageDirectory, filename);
    }

    public async Task AddTip(IUserMessage message, string keywords, string content)
    {
        if (_readOnly)
        {
            await message.Channel.SendMessageAsync("Cannot add or modify tips in the database at this time.");
            return;
        }

        if (string.IsNullOrEmpty(keywords))
        {
            await message.Channel.SendMessageAsync("No valid keywords given to store a new tip.");
            return;
        }

        var keywordList = keywords.Split(',')
            .Select(k => k.Trim())
            .Where(k => IsValidTipKeyword(k))
            .ToList();
        if (keywordList.Count == 0)
        {
            await message.Channel.SendMessageAsync("No valid keywords given to store a new tip.");
            return;
        }

        var imagePaths = new List<string>();
        foreach (var attachment in message.Attachments)
        {
            if (!IsValidTipAttachment(attachment))
                continue;

            var newFileName =
                Guid.NewGuid().ToString() +
                attachment.Filename.Substring(attachment.Filename.LastIndexOf('.'));
            var filePath = GetTipPath(newFileName);

            using var client = new HttpClient();
            await using var stream = await client.GetStreamAsync(attachment.Url);
            await using var file = File.Create(filePath);
            await stream.CopyToAsync(file);
            
            imagePaths.Add(newFileName);
        }

        // Need content and/or a valid attachment
        if (imagePaths.Count == 0 && string.IsNullOrEmpty(content))
        {
            await message.Channel.SendMessageAsync("No valid content given to store a new tip.");
            return;
        }

        ulong id = message.Id;
        var tip = new Tip
        {
            Id = id,
            Content = content,
            Keywords = keywordList,
            ImagePaths = imagePaths
        };

        foreach (var keyword in keywordList)
        {
            _tips.AddOrUpdate(keyword, new List<Tip> { tip }, (key, list) =>
            {
                list.Add(tip);
                return list;
            });
        }

        await CommitTipDatabase();

        string words = string.Join("`, `", keywordList);
        await _loggingService.LogAction(
            $"[{ServiceName}] Added tip from {message.Author.Username} with keywords `{words}`.",
            ExtendedLogSeverity.Info);

        // Send a confirmation message
        if (message.Channel is SocketTextChannel textChannel)
        {
            var builder = new EmbedBuilder()
                .WithTitle("Tip Added")
                .WithDescription($"Your tip has been added with the keywords `{words}` and ID {tip.Id}.")
                .WithColor(Color.Green);

            // TODO: (James) Attach the images if they exist?

            await textChannel.SendMessageAsync(embed: builder.Build());
        }
    }

    public async Task RemoveTip(IUserMessage message, Tip tip)
    {
        if (tip == null)
        {
            await message.Channel.SendMessageAsync("No such tip found to be removed.");
            return;
        }

        foreach (string keyword in tip.Keywords)
        {
            if (!_tips.ContainsKey(keyword))
                continue;
            _tips[keyword].Remove(tip);
            if (_tips[keyword].Count == 0)
                _tips.Remove(keyword, out var _);
        }

        foreach (string imagePath in tip.ImagePaths)
        {
            try
            {
                File.Delete(GetTipPath(imagePath));
            }
            catch (Exception e)
            {
                await _loggingService.LogAction(
                    $"[{ServiceName}] Failed to remove tip image: {e}", ExtendedLogSeverity.Warning);
            }
        }

        await CommitTipDatabase();

        string keywords = string.Join("`, `", tip.Keywords);
        await message.Channel.SendMessageAsync($"Removed a tip with keywords `{keywords}`.");
    }

    public async Task ReplaceTip(IUserMessage message, string keywords, string content)
    {
        if (_readOnly)
        {
            await message.Channel.SendMessageAsync("Cannot add or modify tips in the database at this time.");
            return;
        }

        // TODO: get tip
        // TODO: if not found, bail
        // TODO: remove tip
        // TODO: add tip
        // REVIEW: causes two CommitTipDatabase calls
    }

    private async Task CommitTipDatabase()
    {
        // In same folder, we save json files
        var jsonPath = GetTipPath(DatabaseName);
        await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(_tips));
    }

    public string DumpTipDatabase()
    {
        return JsonConvert.SerializeObject(_tips);
    }

    public Tip GetTip(ulong Id)
    {
        foreach (var kvp in _tips)
            foreach (var tip in kvp.Value)
                if (tip.Id == Id)
                    return tip;
        return null;
    }

    public List<Tip> GetTips(string keywords)
    {
        var found = new List<Tip>();
        if (string.IsNullOrEmpty(keywords))
            return found;

        // TODO: if keywords looks numeric, get one tip based on id

        var keywordList = keywords.Split(',')
            .Select(k => k.Trim())
            .Where(k => IsValidTipKeyword(k))
            .ToList();

        foreach (string keyword in keywordList)
            if (_tips.ContainsKey(keyword))
                foreach (Tip tip in _tips[keyword])
                    if (!found.Contains(tip))
                        found.Add(tip);
        return found;
    }
}
