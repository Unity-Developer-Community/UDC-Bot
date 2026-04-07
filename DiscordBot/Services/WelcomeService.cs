using System.IO;
using Discord.WebSocket;
using DiscordBot.Settings;

namespace DiscordBot.Services;

public class WelcomeService
{
    private const string ServiceName = "WelcomeService";

    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _databaseService;
    private readonly ILoggingService _loggingService;

    private readonly BotSettings _settings;
    private readonly CancellationToken _shutdownToken;

    private readonly List<(ulong id, DateTime time)> _welcomeNoticeUsers = new();

    private readonly Color _welcomeColour = new Color(7, 84, 53);
    public int WaitingWelcomeMessagesCount => _welcomeNoticeUsers.Count;

    public DateTime NextWelcomeMessage =>
        _welcomeNoticeUsers.Any() ? _welcomeNoticeUsers.Min(x => x.time) : DateTime.MaxValue;

    public WelcomeService(DiscordSocketClient client, DatabaseService databaseService, ILoggingService loggingService,
        BotSettings settings, CancellationTokenSource cts)
    {
        _client = client;
        _databaseService = databaseService;
        _loggingService = loggingService;
        _settings = settings;
        _shutdownToken = cts.Token;

        /* Make sure folders we require exist */
        if (!Directory.Exists($"{_settings.ServerRootPath}/images/profiles/"))
        {
            Directory.CreateDirectory($"{_settings.ServerRootPath}/images/profiles/");
        }

        /*
         Event subscriptions
        */
        _client.UserJoined += EventGuard.Guarded<SocketGuildUser>(UserJoined, nameof(UserJoined));

        _client.MessageReceived += EventGuard.Guarded<SocketMessage>(CheckForWelcomeMessage, nameof(CheckForWelcomeMessage));
        _client.UserIsTyping += EventGuard.Guarded<Cacheable<IUser, ulong>, Cacheable<IMessageChannel, ulong>>(UserIsTyping, nameof(UserIsTyping));

        Task.Run(DelayedWelcomeService);
    }

    public Embed WelcomeMessage(SocketGuildUser user)
    {
        string icon = user.GetAvatarUrl();
        icon = string.IsNullOrEmpty(icon) ? "https://cdn.discordapp.com/embed/avatars/0.png" : icon;

        string welcomeString = $"Welcome to Unity Developer Community, {user.GetPreferredAndUsername()}!";
        var builder = new EmbedBuilder()
            .WithDescription(welcomeString)
            .WithColor(_welcomeColour)
            .WithAuthor(user.GetUserPreferredName(), icon);

        var embed = builder.Build();
        return embed;
    }

    #region Events

    // Anything relevant to the first time someone connects to the server

    #region Welcome Service

    // If a user talks before they've been welcomed, we welcome them and remove them from the welcome list so they're not welcomes a second time.
    private async Task UserIsTyping(Cacheable<IUser, ulong> user, Cacheable<IMessageChannel, ulong> channel)
    {
        if (_welcomeNoticeUsers.Count == 0)
            return;
        if (user.Value.IsBot)
            return;

        if (_welcomeNoticeUsers.Exists(u => u.id == user.Id))
        {
            _welcomeNoticeUsers.RemoveAll(u => u.id == user.Id);
            await ProcessWelcomeUser(user.Id, user.Value);
        }
    }

    private async Task CheckForWelcomeMessage(SocketMessage messageParam)
    {
        if (_welcomeNoticeUsers.Count == 0)
            return;

        var user = messageParam.Author;
        if (user.IsBot)
            return;

        if (_welcomeNoticeUsers.Exists(u => u.id == user.Id))
        {
            _welcomeNoticeUsers.RemoveAll(u => u.id == user.Id);
            await ProcessWelcomeUser(user.Id, user);
        }
    }

    private async Task UserJoined(SocketGuildUser user)
    {
        // Send them the Welcome DM first.
        await DMFormattedWelcome(user);

        var socketTextChannel = _client.GetChannel(_settings.GeneralChannel.Id) as SocketTextChannel;
        await _databaseService.GetOrAddUser(user);

        await _loggingService.LogChannelAndFile(
            $"User Joined - {user.Mention} - `{user.GetPreferredAndUsername()}` - ID : `{user.Id}`");

        // We check if they're already in the welcome list, if they are we don't add them again to avoid double posts
        if (_welcomeNoticeUsers.Count == 0 || !_welcomeNoticeUsers.Exists(u => u.id == user.Id))
        {
            _welcomeNoticeUsers.Add((user.Id, DateTime.Now.AddSeconds(_settings.WelcomeMessageDelaySeconds)));
        }
    }

    // Welcomes users to the server after they've been connected for over x number of seconds.
    private async Task DelayedWelcomeService()
    {
        ulong currentlyProcessedUserId = 0;
        bool firstRun = true;
        await Task.Delay(10000, _shutdownToken);
        try
        {
            List<ulong> toRemove = new();
            while (!_shutdownToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                // This could be optimized, however the users in this list won't ever really be large enough to matter.
                // We loop through our list, anyone that has been in the list for more than x seconds is welcomed.
                foreach (var userData in _welcomeNoticeUsers.Where(u => u.time < now))
                {
                    currentlyProcessedUserId = userData.id;
                    await ProcessWelcomeUser(userData.id, null);

                    toRemove.Add(userData.id);
                }

                // Remove all the users we've welcomed from the list
                if (toRemove.Count > 0)
                {
                    _welcomeNoticeUsers.RemoveAll(u => toRemove.Contains(u.id));
                    toRemove.Clear();
                    // Prevent the list from growing too large, not that it really matters.
                    if (toRemove.Capacity > 20)
                    {
                        toRemove.Capacity = 20;
                    }
                }

                if (firstRun)
                    firstRun = false;
                await Task.Delay(10000, _shutdownToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            // Catch and show exception
            await _loggingService.LogChannelAndFile($"{ServiceName} Exception during welcome message `{currentlyProcessedUserId}`.\n{e.Message}.", ExtendedLogSeverity.Warning);

            // Remove the offending user from the dictionary and run the service again.
            _welcomeNoticeUsers.RemoveAll(u => u.id == currentlyProcessedUserId);
            if (_welcomeNoticeUsers.Count > 200)
            {
                _welcomeNoticeUsers.Clear();
                await _loggingService.LogAction($"{ServiceName}: Welcome list cleared due to size (+200), this should not happen.", ExtendedLogSeverity.Error);
            }

            if (firstRun)
                await _loggingService.LogAction($"{ServiceName}: Welcome service failed on first run!? This should not happen.", ExtendedLogSeverity.Error);

            // Restart unless shutdown was requested
            if (!_shutdownToken.IsCancellationRequested)
                _ = Task.Run(DelayedWelcomeService);
        }
    }

    private async Task ProcessWelcomeUser(ulong userID, IUser? user = null)
    {
        if (_welcomeNoticeUsers.Exists(u => u.id == userID))
            // If we didn't get the user passed in, we try grab it
            user ??= await _client.GetUserAsync(userID);
        // if they're null, they've likely left, so we just remove them from the list.
        if (user == null)
            return;

        var offTopic = await _client.GetChannelAsync(_settings.GeneralChannel.Id) as SocketTextChannel;
        if (user is not SocketGuildUser guildUser)
            return;
        var em = WelcomeMessage(guildUser);
        if (offTopic != null && em != null)
            await offTopic.SendMessageAsync(string.Empty, false, em);
    }


    public async Task<bool> DMFormattedWelcome(SocketGuildUser user)
    {
        var dm = await user.CreateDMChannelAsync();
        return await dm.TrySendMessage(embed: GetWelcomeEmbed(user.Username));
    }

    public Embed GetWelcomeEmbed(string username = "")
    {
        //TODO Generate this using Settings or some other config, hardcoded isn't ideal.
        var em = new EmbedBuilder()
            .WithColor(new Color(0x12D687))
            .AddField("Hello " + username,
                "Welcome to Unity Developer Community!\nPlease read and respect the rules to keep the community friendly!\n*When asking questions, remember to ask your question, [don't ask to ask](https://dontasktoask.com/).*")
            .AddField("__RULES__",
                ":white_small_square: Be polite and respectful.\n" +
                ":white_small_square: No Direct Messages to users without permission.\n" +
                ":white_small_square: Do not post the same question in multiple channels.\n" +
                ":white_small_square: Only post links to your games in the appropriate channels.\n" +
                ":white_small_square: Some channels have additional rules, please check pinned messages.\n" +
                $":white_small_square: A more inclusive list of rules can be found in {(_settings.RulesChannel is null || _settings.RulesChannel.Id == 0 ? "#rules" : $"<#{_settings.RulesChannel.Id.ToString()}>")}"
            )
            .AddField("__PROGRAMMING RESOURCES__",
                ":white_small_square: Official Unity [Manual](https://docs.unity3d.com/Manual/index.html)\n" +
                ":white_small_square: Official Unity [Script API](https://docs.unity3d.com/ScriptReference/index.html)\n" +
                ":white_small_square: Introductory Tutorials: [Official Unity Tutorials](https://unity3d.com/learn/tutorials)\n" +
                ":white_small_square: Intermediate Tutorials: [CatLikeCoding](https://catlikecoding.com/unity/tutorials/)\n"
            )
            .AddField("__ART RESOURCES__",
                ":white_small_square: Blender Beginner Tutorial [Blender Guru Donut](https://www.youtube.com/watch?v=TPrnSACiTJ4&list=PLjEaoINr3zgEq0u2MzVgAaHEBt--xLB6U&index=2)\n" +
                ":white_small_square: Free Simple Assets [Kenney](https://www.kenney.nl/assets)\n" +
                ":white_small_square: Game Assets [itch.io](https://itch.io/game-assets/free)"
            )
            .AddField("__GAME DESIGN RESOURCES__",
                ":white_small_square: How to write a Game Design Document (GDD) [Gamasutra](https://www.gamasutra.com/blogs/LeandroGonzalez/20160726/277928/How_to_Write_a_Game_Design_Document.php)\n" +
                ":white_small_square: How to start building video games [CGSpectrum](https://www.cgspectrum.com/blog/game-design-basics-how-to-start-building-video-games)\n" +
                ":white_small_square: Keep Things Clear: Don't Confuse Your Players [TutsPlus](https://gamedevelopment.tutsplus.com/articles/keep-things-clear-dont-confuse-your-players--cms-22780)"
            );
        return (em.Build());
    }

    #endregion

    #endregion
}
