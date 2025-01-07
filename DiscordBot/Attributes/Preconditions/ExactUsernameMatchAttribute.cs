using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace DiscordBot.Attributes.Preconditions;

/**
 * Meant to be used with commands that require IUser but don't want to allow any variations of the username ie; "UserName" shouldn't match "UserName#1123" or "UserName1234"
 * You can see this in the WeatherModule as the Temperature and Weather commands, used to avoid "!weather paris" matching a user Paris followed by a few numbers.
 */
public class ExactUsernameMatchAttribute : PreconditionAttribute
{
	public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
	{
		var message = context.Message.Content;
		var users = (context.Guild as SocketGuild)?.Users;

		if (users == null)
		{
			return Task.FromResult(PreconditionResult.FromError("Unable to retrieve users."));
		}

		var username = message.Split(' ').Skip(1).FirstOrDefault();
		if (username == null)
		{
			return Task.FromResult(PreconditionResult.FromError("No username provided."));
		}

		var exactMatch = users.Any(user => user.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
		if (exactMatch)
		{
			return Task.FromResult(PreconditionResult.FromSuccess());
		}

		return Task.FromResult(PreconditionResult.FromError("Username does not match exactly."));
	}
}