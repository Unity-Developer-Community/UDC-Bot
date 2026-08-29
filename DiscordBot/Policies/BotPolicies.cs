using Discord.WebSocket;
using DiscordBot.Services.UnityHelp;
using DiscordBot.Settings.Options;
using DiscordBot.Settings.Validation;
using Microsoft.Extensions.Options;

namespace DiscordBot.Policies;

public interface ICommandChannelPolicy
{
    bool IsCommandChannel(ulong channelId);
    bool IsCommandOrGeneralChannel(ulong channelId);
    string CommandChannelMention { get; }
    string CommandOrGeneralChannelMentions { get; }
    Task<IMessageChannel?> GetCommandChannelAsync(IGuild guild);
}

public sealed class CommandChannelPolicy(
    IOptions<CommandOptions> commandOptions,
    IOptions<ModerationOptions> moderationOptions) : ICommandChannelPolicy
{
    private readonly CommandOptions _commands = commandOptions.Value;
    private readonly ModerationOptions _moderation = moderationOptions.Value;

    public string CommandChannelMention => Mention(_commands.BotCommandsChannelId);
    public string CommandOrGeneralChannelMentions =>
        $"{CommandChannelMention} or {Mention(_moderation.GeneralChannelId)}";

    public bool IsCommandChannel(ulong channelId) => channelId == _commands.BotCommandsChannelId;

    public bool IsCommandOrGeneralChannel(ulong channelId) =>
        IsCommandChannel(channelId) || channelId == _moderation.GeneralChannelId;

    public async Task<IMessageChannel?> GetCommandChannelAsync(IGuild guild) =>
        await guild.GetChannelAsync(_commands.BotCommandsChannelId) as IMessageChannel;

    private static string Mention(ulong id) => id == 0 ? "the configured channel" : $"<#{id}>";
}

public interface IBotAuthorizationPolicy
{
    bool IsModerator(SocketGuildUser user);
    IRole? GetModeratorRole(IGuild guild);
}

public sealed class BotAuthorizationPolicy(IOptions<AuthorizationOptions> options) : IBotAuthorizationPolicy
{
    private readonly ulong _moderatorRoleId = options.Value.ModeratorRoleId;

    public bool IsModerator(SocketGuildUser user) => user.Roles.Any(role => role.Id == _moderatorRoleId);
    public IRole? GetModeratorRole(IGuild guild) => guild.GetRole(_moderatorRoleId);
}

public interface IRoleAssignmentPolicy
{
    IReadOnlyList<string> AssignableRoles { get; }
    bool IsAssignable(string roleName);
}

public sealed class RoleAssignmentPolicy(IOptions<RoleAssignmentOptions> options) : IRoleAssignmentPolicy
{
    private readonly HashSet<string> _roles = options.Value.AssignableRoles
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> AssignableRoles => _roles.OrderBy(role => role).ToArray();
    public bool IsAssignable(string roleName) => _roles.Contains(roleName);
}

public interface IBotPublicInfo
{
    string Invite { get; }
}

public sealed class BotPublicInfo(IOptions<DiscordGuildOptions> options) : IBotPublicInfo
{
    public string Invite { get; } = options.Value.Invite;
}

public interface IModerationPolicy
{
    bool CommandsEnabled { get; }
    bool IsMuted(IGuildUser user);
    IRole? GetMutedRole(IGuild guild);
    Task<IMessageChannel?> GetAnnouncementChannelAsync(IGuild guild);
    Task<IMessageChannel?> GetReportedMessageChannelAsync(IGuild guild);
}

public sealed class ModerationPolicy(
    IOptions<ModerationOptions> moderationOptions,
    IOptions<LoggingOptions> loggingOptions) : IModerationPolicy
{
    private readonly ModerationOptions _moderation = moderationOptions.Value;
    private readonly ulong _announcementChannelId = loggingOptions.Value.AnnouncementChannelId;

    public bool CommandsEnabled => _moderation.CommandsEnabled;
    public bool IsMuted(IGuildUser user) => user.RoleIds.Contains(_moderation.MutedRoleId);
    public IRole? GetMutedRole(IGuild guild) => guild.GetRole(_moderation.MutedRoleId);

    public async Task<IMessageChannel?> GetAnnouncementChannelAsync(IGuild guild) =>
        await guild.GetChannelAsync(_announcementChannelId) as IMessageChannel;

    public async Task<IMessageChannel?> GetReportedMessageChannelAsync(IGuild guild) =>
        await guild.GetChannelAsync(_moderation.ReportedMessageChannelId) as IMessageChannel;
}

public interface ITicketPolicy
{
    ulong OpenCategoryId { get; }
    ulong ClosedCategoryId { get; }
    string OpenChannelPrefix { get; }
    string ClosedChannelPrefix { get; }
}

public sealed class TicketPolicy(IOptions<TicketOptions> options) : ITicketPolicy
{
    private readonly TicketOptions _options = options.Value;

    public ulong OpenCategoryId => _options.OpenCategoryId;
    public ulong ClosedCategoryId => _options.ClosedCategoryId;
    public string OpenChannelPrefix => _options.OpenChannelPrefix;
    public string ClosedChannelPrefix => _options.ClosedChannelPrefix;
}

public interface IUnityHelpPolicy
{
    bool IsAvailable { get; }
    bool IsHelpThread(IMessageChannel channel);
    string HelpForumMention { get; }
}

public sealed class UnityHelpPolicy(
    IOptions<UnityHelpOptions> options,
    FeatureConfigurationCatalog featureConfiguration) : IUnityHelpPolicy
{
    private readonly UnityHelpOptions _options = options.Value;

    public bool IsAvailable => _options.Enabled && featureConfiguration.Get("unity-help").IsConfigured;
    public string HelpForumMention => _options.ForumChannelId == 0
        ? "the configured Unity Help forum"
        : $"<#{_options.ForumChannelId}>";

    public bool IsHelpThread(IMessageChannel channel) => channel.IsThreadInChannel(_options.ForumChannelId);
}

public interface IUserFunPolicy
{
    string SlapObjectsTable { get; }
    IReadOnlyList<string> SlapChoices { get; }
    IReadOnlyList<string> SlapFailures { get; }
}

public sealed class UserFunPolicy(IOptions<UserFunOptions> options) : IUserFunPolicy
{
    private readonly UserFunOptions _options = options.Value;

    public string SlapObjectsTable => _options.SlapObjectsTable;
    public IReadOnlyList<string> SlapChoices => _options.SlapChoices;
    public IReadOnlyList<string> SlapFailures => _options.SlapFailures;
}

public interface ITipsAuthorizationPolicy
{
    bool CanManageTips(SocketGuildUser user);
}

public sealed class TipsAuthorizationPolicy(
    IBotAuthorizationPolicy authorization,
    IOptions<TipsOptions> options) : ITipsAuthorizationPolicy
{
    private readonly ulong _helperRoleId = options.Value.HelperRoleId;

    public bool CanManageTips(SocketGuildUser user) =>
        authorization.IsModerator(user) || user.Roles.Any(role => role.Id == _helperRoleId);
}
