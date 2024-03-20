using Discord.Commands;
using Discord.WebSocket;
using DiscordBot.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireThreadAttribute : PreconditionAttribute
{
    protected SocketThreadChannel _currentThread;

    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        this._currentThread = context.Message.Channel as SocketThreadChannel;
        if (this._currentThread != null) return await Task.FromResult(PreconditionResult.FromSuccess());

        Task task = context.Message.DeleteAfterSeconds(seconds: 10);
        return await Task.FromResult(PreconditionResult.FromError("This command can only be used in a thread."));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAutoThreadAttribute : RequireThreadAttribute
{
    protected AutoThreadChannel _autoThreadChannel;

    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var res = await base.CheckPermissionsAsync(context, command, services);
        if (!res.IsSuccess) return res;

        var settings = services.GetRequiredService<BotSettings>();
        this._autoThreadChannel = settings.AutoThreadChannels.Find(x => this._currentThread.ParentChannel.Id == x.Id);
        if (this._autoThreadChannel != null) return await Task.FromResult(PreconditionResult.FromSuccess());

        Task task = context.Message.DeleteAfterSeconds(seconds: 10);
        return await Task.FromResult(PreconditionResult.FromError("This command can only be used in a thread created automatically."));

    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireArchivableAutoThreadAttribute : RequireAutoThreadAttribute
{
    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var res = await base.CheckPermissionsAsync(context, command, services);
        if (!res.IsSuccess) return res;

        if (this._autoThreadChannel.CanArchive) return await Task.FromResult(PreconditionResult.FromSuccess());

        Task task = context.Message.DeleteAfterSeconds(seconds: 10);
        return await Task.FromResult(PreconditionResult.FromError("This command cannot be used in a this thread."));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireDeletableAutoThreadAttribute : RequireAutoThreadAttribute
{
    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var res = await base.CheckPermissionsAsync(context, command, services);
        if (!res.IsSuccess) return res;

        if (this._autoThreadChannel.CanDelete) return await Task.FromResult(PreconditionResult.FromSuccess());

        Task task = context.Message.DeleteAfterSeconds(seconds: 10);
        return await Task.FromResult(PreconditionResult.FromError("This command cannot be used in a this thread."));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAutoThreadAuthorAttribute : RequireAutoThreadAttribute
{
    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var res = await base.CheckPermissionsAsync(context, command, services);
        if (!res.IsSuccess) return res;

        var messages = await this._currentThread.GetPinnedMessagesAsync();
        var firstMessage = messages.LastOrDefault();
        if (firstMessage != null)
        {
            var user = (SocketGuildUser)context.Message.Author;
            if (firstMessage.MentionedUsers.Any(x => x.Id == context.User.Id))
                return await Task.FromResult(PreconditionResult.FromSuccess());
        }

        Task task = context.Message.DeleteAfterSeconds(seconds: 10);
        return await Task.FromResult(PreconditionResult.FromError("This command can only be used by the thread author."));
    }
}