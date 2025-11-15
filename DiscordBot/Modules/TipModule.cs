using System.IO;
using Discord.Commands;
using DiscordBot.Attributes;
using DiscordBot.Services;
using DiscordBot.Services.Tips;
using DiscordBot.Services.Tips.Components;
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

	private bool IsAuthorized(IUser user)
	{
		if (user.HasRoleGroup(Settings.ModeratorRoleId))
			return true;
		if (user.HasRoleGroup(Settings.TipsUserRoleId))
			return true;

		return false;
 	}

	[Command("Tip")]
	[Summary("Find and provide pre-authored tips (images or text) by their keywords.")]
 	/* removing [RequireModerator] for custom check */
	public async Task Tip(params string[] keywords)
	{
		var user = Context.Message.Author;
		if (!IsAuthorized(user))
			return;

		var terms = string.Join(",", keywords);
		var tips = TipService.GetTips(terms);
		if (tips.Count == 0)
		{
			await ReplyAsync("No tips for the keywords provided were found.").DeleteAfterSeconds(5);
			return;
		}

		foreach (var tip in tips)
  			tip.Requests++;

		var isAnyTextTips = tips.Any(tip => !string.IsNullOrEmpty(tip.Content));
		var builder = new EmbedBuilder();
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
			.Select(imagePath => new FileAttachment(TipService.GetTipPath(imagePath)))
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

		var ids = string.Join(" ", tips.Select(t => t.Id.ToString()).ToArray());
		await ReplyAsync($"-# Tip ID {ids}");
		await Context.Message.DeleteAsync();
  		await TipService.CommitTipDatabase();
	}
	
	[Command("AddTip")]
	[Summary("Add a tip to the database.")]
	[RequireModerator]
	public async Task AddTip(string keywords, string content = "")
	{
		await TipService.AddTip(Context.Message, keywords, content);
	}
	
	[Command("RemoveTip")]
	[Summary("Remove a tip from the database.")]
	[RequireModerator]
	public async Task RemoveTip(ulong tipId)
	{
 		Tip tip = TipService.GetTip(tipId);
   		if (tip == null)
	 	{
			await Context.Channel.SendMessageAsync("No such tip found to be removed.").DeleteAfterSeconds(5);
			return;
   		}
 
		await TipService.RemoveTip(Context.Message, tip);
	}

	[Command("ReplaceTip")]
	[Summary("Replace image content of an existing tip in the database.")]
	[RequireModerator]
	public async Task ReplaceTip(ulong tipId, string content = "")
	{
 		Tip tip = TipService.GetTip(tipId);
   		if (tip == null)
	 	{
			await Context.Channel.SendMessageAsync("No such tip found to be replaced.").DeleteAfterSeconds(5);
			return;
   		}
 
		await TipService.ReplaceTip(Context.Message, tip, content);
	}

	[Command("ReloadTips")]
	[Summary("Reload the database of tips.")]
	[RequireModerator]
	public async Task ReloadTipDatabase()
	{
 		// rare usage, but in case someone with a shell decides
   		// to edit the json for debugging/expansion reasons...
 		await TipService.ReloadTipDatabase();
   		await ReplyAsync("Tip index reloaded.");
	}
	
	[Command("ListTips")]
	[Summary("List available tips by their keywords.")]
	/* removing [RequireModerator] for custom check */
	public async Task ListTips(params string[] keywords)
	{
		var user = Context.Message.Author;
		if (!IsAuthorized(user))
			return;

   		int floodCount = 10;

		List<Tip> tips = null;
  		if (keywords?.Length > 0)
		{
			var terms = string.Join(",", keywords);
			tips = TipService.GetTips(terms);
			if (tips.Count == 0)
			{
				await ReplyAsync("No tips for the keywords provided were found.").DeleteAfterSeconds(5);
				return;
			}
			if (tips.Count >= floodCount)
			{
				await ReplyAsync($"Total of {tips.Count} tips found for the keywords provided; refine your search.").DeleteAfterSeconds(5);
				return;
			}
		}
		else
  		{
			tips = TipService.GetAllTips().OrderBy(t => t.Id).ToList();
			if (tips.Count >= floodCount)
			{
				var terms = new HashSet<string>();
				foreach (var tip in tips)
					foreach (var term in tip.Keywords)
						terms.Add(term);
				await ReplyAsync($"Total of {tips.Count} tips found, add one or more keywords to narrow the search.");
				var termList = new List<string>();
				foreach (var tip in terms.OrderBy(k => k))
					termList.Add(tip);
				floodCount = 8;
				while (termList.Count > 0)
				{
					int count = termList.Count;
					if (count > floodCount)
						count = floodCount-2;
					string keywordList = "Keywords: ";
					for (int i = 0; i < count; i++)
					{
						keywordList = $"`{termList[0]}`, ";
						termList.RemoveAt(0);
					}
					keywordList = keywordList.Substring(0, keywordList.Length-2);
					await ReplyAsync(keywordList);
					if (termList.Count > 0)
		   				await Task.Delay(500);
				}
				return;
			}
   		}

   		int chunkCount = 10;
	 	int chunkTime = 1500;
   		bool first = true;

		while (tips.Count > 0)
  		{
			var builder = new EmbedBuilder();
			if (first)
			{
				builder
					.WithTitle("List of Tips")
					.WithDescription("Tips available for the following keywords:");
				first = false;
			}

			int chunk = 0;
			while (tips.Count > 0 && chunk < chunkCount)
   			{
				string keywordlist = string.Join("`, `", tips[0].Keywords.OrderBy(k => k));
				string images = String.Concat(
    					Enumerable.Repeat(" :frame_photo:",
	 				tips[0].ImagePaths.Count).ToArray());
				builder.AddField($"ID: {tips[0].Id} {images}", $"`{keywordlist}`");
				tips.RemoveAt(0);
				chunk++;
			}

			await ReplyAsync(embed: builder.Build());
			if (tips.Count > 0)
   				await Task.Delay(chunkTime);
	   	}
	}

	#region CommandList
	[Command("TipHelp")]
	[Alias("TipsHelp")]
	[Summary("Shows available tip database commands.")]
	public async Task TipHelp()
	{
 		// NOTE: skips the RequireModerator commands, so nearly an empty list
		foreach (var message in CommandHandlingService.GetCommandListMessages("TipModule", true, true, false))
		{
			await ReplyAsync(message);
		}
	}
	#endregion
	
}
