using System.IO;
using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Services;
using DiscordBot.Services.Tips;
using DiscordBot.Settings;

// ReSharper disable all UnusedMember.Local
namespace DiscordBot.Modules;

public class TipModule : ModuleBase
{
	#region Dependency Injection

	public CommandHandlingService CommandHandlingService { get; set; }
	public BotSettings Settings { get; set; }
	public TipService TipService { get; set; }

	#endregion
	
	[Command("Tip")]
	[Summary("Find a Unity or general development tip.")]
	public async Task Tip(string keywords)
	{
		var tips = TipService.GetTips(keywords);
		if (tips.Count == 0)
		{
			await ReplyAsync("No tips for the keywords provided were found.");
			return;
		}

		var isAnyTextTips = tips.Any(tip => !string.IsNullOrEmpty(tip.Content));
		EmbedBuilder builder = new EmbedBuilder();
		if (isAnyTextTips)
		{
			// Loop through tips in order, have dot point list of the .Content property in an embed
			builder
				.WithTitle("Tip List")
				.WithDescription("Here are the tips for your keywords:");
			foreach (var tip in tips)
			{
				builder.AddField(tip.Keywords.Count == 1 ? tip.Keywords[0] : "Multiple Keywords", tip.Content);
			}
		}
		
		var attachments = tips
			.Where(tip => tip.ImagePaths != null && tip.ImagePaths.Any())
			.SelectMany(tip => tip.ImagePaths)
			.Select(imagePath => new FileAttachment(Path.Combine(Settings.TipImageDirectory, imagePath)))
			.ToList();

		if (attachments.Count > 0)
		{
			if (isAnyTextTips)
			{
				await Context.Channel.SendFilesAsync(attachments, embed: builder.Build());
			}
			else
			{
				await Context.Channel.SendFilesAsync(attachments);
			}
		}
		else
		{
			await ReplyAsync(embed: builder.Build());
		}
	}
	
	[Command("AddTip")]
	[Summary("Add a tip to the database.")]
	[RequireModerator]
	public async Task AddTip(string keywords, string content = "")
	{
		await TipService.AddTip(Context.Message, keywords, content);
	}

	#region CommandList
	
	[Summary("Does what you see now.")]
	[Command("Ticket Help")]
	public async Task TicketHelp()
	{
		foreach (var message in CommandHandlingService.GetCommandListMessages("TipModule", true, true, false))
		{
			await ReplyAsync(message);
		}
	}
	#endregion
	
}