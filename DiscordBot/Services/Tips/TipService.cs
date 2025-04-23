using System.Collections.Concurrent;
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
    
    private readonly BotSettings _settings;
    private readonly ILoggingService _loggingService;
    private readonly string _imageDirectory;

    private ConcurrentDictionary<string, List<Tip>> _tips = new();
    private bool _isRunning = false;
    
    public TipService(BotSettings settings, ILoggingService loggingService)
    {
        _settings = settings;
        _loggingService = loggingService;
        
        if (string.IsNullOrEmpty(_settings.TipImageDirectory))
        {
            _loggingService.LogAction($"[{ServiceName}] TipImageDirectory not set, service will not run.", ExtendedLogSeverity.Warning);
            _isRunning = false;
            return;
        }
        
        _imageDirectory = Path.Combine(Directory.GetCurrentDirectory(), _settings.TipImageDirectory);

        Initialize();
    }
    
    private void Initialize()
    {
        if (_isRunning) return;
        
        if (!Directory.Exists(_imageDirectory))
        {
            Directory.CreateDirectory(_imageDirectory);
            File.WriteAllText(Path.Combine(_imageDirectory, "tips.json"), "{}");
        }
        else
        {
            var directorySize = new DirectoryInfo(_imageDirectory).EnumerateFiles("*.*", SearchOption.AllDirectories).Sum(file => file.Length);
            if (directorySize > _settings.TipMaxDirectoryFileSize)
            {
                _loggingService.LogAction($"[{ServiceName}] Tip directory size is {directorySize / 1024 / 1024} MB, exceeding the limit of {_settings.TipMaxDirectoryFileSize / 1024 / 1024} MB, no additional content will be added during this session.", ExtendedLogSeverity.Warning);
            }
            else
            {
                _loggingService.LogAction($"[{ServiceName}] Tip directory size is {directorySize / 1024 / 1024} MB, within the limit of {_settings.TipMaxDirectoryFileSize / 1024 / 1024} MB.", ExtendedLogSeverity.Info);
                _loggingService.LogAction($"[{ServiceName}] Tip directory contains {new DirectoryInfo(_imageDirectory).EnumerateFiles("*.*", SearchOption.AllDirectories).Count()} files.",
                    ExtendedLogSeverity.Info);
            }
            
            var jsonPath = Path.Combine(_imageDirectory, "tips.json");
            if (File.Exists(jsonPath))
            {
                var json =  File.ReadAllText(jsonPath);
                _tips = JsonConvert.DeserializeObject<ConcurrentDictionary<string, List<Tip>>>(json);
            }
        }

        _isRunning = true;
    }

    public async Task AddTip(IUserMessage message, string keywords, string content)
    {
        var keywordList = keywords.Split(',').Select(k => k.Trim()).ToList();
        var imagePaths = new List<string>();

        foreach (var attachment in message.Attachments)
        {
            if (!attachment.Filename.EndsWith(".png") && !attachment.Filename.EndsWith(".webp") && !attachment.Filename.EndsWith(".jpg")) continue;
            var newFileName = Guid.NewGuid().ToString() + attachment.Filename.Substring(attachment.Filename.LastIndexOf('.'));
            var filePath = Path.Combine(_imageDirectory, newFileName);
            if (attachment.Size > _settings.TipMaxImageFileSize)
            {
                continue;
            }

            using var client = new HttpClient();
            await using var stream = await client.GetStreamAsync(attachment.Url);
            await using var file = File.Create(filePath);
            await stream.CopyToAsync(file);
            
            imagePaths.Add(newFileName);
        }

        var tip = new Tip
        {
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

        // In same folder, we save json files
        var jsonPath = Path.Combine(_imageDirectory, "tips.json");
        await File.WriteAllTextAsync(jsonPath, JsonConvert.SerializeObject(_tips));

        await _loggingService.LogAction($"[{ServiceName}] Added tip from {message.Author.Username} with keywords {string.Join(", ", keywordList)}.", ExtendedLogSeverity.Info);
        
        // Send a confirmation message
        if (message.Channel is SocketTextChannel textChannel)
        {
            var builder = new EmbedBuilder()
                .WithTitle("Tip Added")
                .WithDescription($"Your tip has been added with the keywords `{string.Join(", ", keywordList)}`.")
                .WithColor(Color.Green);
            
            // TODO: (James) Attach the images if they exist?

            await textChannel.SendMessageAsync(embed: builder.Build());
        }
    }

    public List<Tip> GetTips(string keyword)
    {
        var regex = new Regex(keyword, RegexOptions.IgnoreCase);
        return _tips.Where(kvp => kvp.Key.Split(',').Any(k => regex.IsMatch(k)))
            .SelectMany(kvp => kvp.Value)
            .Distinct()
            .ToList();
    }
}
