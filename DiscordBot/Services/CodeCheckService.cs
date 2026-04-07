using System.Text.RegularExpressions;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class CodeCheckService
{
    private readonly DiscordSocketClient _client;
    private readonly BotSettings _settings;
    private readonly UpdateService _updateService;
    private readonly CancellationToken _shutdownToken;

    private readonly Regex _x3CodeBlock =
        new("^(?<CodeBlock>`{3}((?<CS>\\w*?$)|$).+?({.+?}).+?`{3})", RegexOptions.Multiline | RegexOptions.Singleline);

    private readonly Regex _x2CodeBlock = new("^(`{2})[^`].+?([^`]`{2})$", RegexOptions.Multiline);
    private readonly List<Regex> _codeBlockWarnPatterns;
    private readonly short _maxCodeBlockLengthWarning = 800;

    public readonly string CodeFormattingExample;
    private readonly string _codeReminderFormattingExample;
    public Dictionary<ulong, DateTime> CodeReminderCooldown { get; private set; }

    public CodeCheckService(DiscordSocketClient client, BotSettings settings,
        UpdateService updateService, CancellationTokenSource cts)
    {
        _client = client;
        _settings = settings;
        _updateService = updateService;
        _shutdownToken = cts.Token;

        CodeReminderCooldown = new Dictionary<ulong, DateTime>();

        CodeFormattingExample = @"\`\`\`cs" + Environment.NewLine +
                                "Write your code on new line here." + Environment.NewLine +
                                @"\`\`\`" + Environment.NewLine;

        _codeReminderFormattingExample = CodeFormattingExample + "*To disable these reminders use \"!disablecodetips\"*";

        _codeBlockWarnPatterns = new List<Regex>
        {
            new(".*?({.+?}).*?", RegexOptions.Singleline),
            new("(if|else\\sif).?\\(.+\\).?($|\\/{2}|\\s?)", RegexOptions.Multiline),
            new("^(\\w*.\\w*)\\(\\w*?\\);($|.?($|.*?\\/{2}))", RegexOptions.Multiline),
            new("^.+? =.+?($|.*?\\/\\/)", RegexOptions.Multiline)
        };

        _client.MessageReceived += EventGuard.Guarded<SocketMessage>(CodeCheck, nameof(CodeCheck));

        LoadData();
        UpdateLoop();
    }

    private async void UpdateLoop()
    {
        try
        {
            while (!_shutdownToken.IsCancellationRequested)
            {
                await Task.Delay(10000, _shutdownToken);
                SaveData();
            }
        }
        catch (OperationCanceledException) { SaveData(); }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"[CodeCheckService.UpdateLoop] Unhandled exception: {e}", LogSeverity.Error);
        }
    }

    private void LoadData()
    {
        var data = _updateService.GetUserData();
        CodeReminderCooldown = data.CodeReminderCooldown ?? new Dictionary<ulong, DateTime>();
    }

    private void SaveData()
    {
        var data = new UserData
        {
            CodeReminderCooldown = CodeReminderCooldown
        };
        _updateService.SetUserData(data);
    }

    public async Task CodeCheck(SocketMessage messageParam)
    {
        if (messageParam.Author.IsBot || messageParam.Channel.Id == _settings.Channels.General.Id)
            return;

        if (messageParam.Content.Length < 200)
            return;

        var userId = messageParam.Author.Id;

        if (!CodeReminderCooldown.HasUser(userId))
        {
            var content = messageParam.Content;

            var foundTrippleCodeBlock = _x3CodeBlock.Match(content);
            if (foundTrippleCodeBlock.Groups["CS"].Length > 0)
                return;
            if (foundTrippleCodeBlock.Groups["CodeBlock"].Success)
            {
                await (messageParam.Channel.SendMessageAsync(
                        $"{messageParam.Author.Mention} when using code blocks remember to use the ***syntax highlights*** to improve readability.\n{_codeReminderFormattingExample}")
                    .DeleteAfterSeconds(seconds: 60) ?? Task.CompletedTask);
                return;
            }

            var foundDoubleCodeBlock = _x2CodeBlock.Match(content).Success;

            int hits = 0;
            foreach (var regex in _codeBlockWarnPatterns)
            {
                hits += regex.Match(content).Captures.Count;
            }

            if (!foundDoubleCodeBlock && hits >= 3)
            {
                await (messageParam.Channel.SendMessageAsync(
                        $"{messageParam.Author.Mention} are you sharing C# scripts? Remember to use codeblocks to help readability!\n{_codeReminderFormattingExample}")
                    .DeleteAfterSeconds(seconds: 60) ?? Task.CompletedTask);
                if (content.Length > _maxCodeBlockLengthWarning)
                {
                    await (messageParam.Channel.SendMessageAsync(
                        "The code you're sharing is quite long, maybe use a free service like <https://hastebin.com> and share the link here instead.")
                        .DeleteAfterSeconds(seconds: 60) ?? Task.CompletedTask);
                }
            }
            else if (foundDoubleCodeBlock && hits > 0)
            {
                await (messageParam.Channel.SendMessageAsync(
                        $"{messageParam.Author.Mention} when using code blocks remember to use \\`\\`\\`cs as this will help improve readability for C# scripts.\n{_codeReminderFormattingExample}")
                    .DeleteAfterSeconds(seconds: 60) ?? Task.CompletedTask);
            }
        }
    }
}
