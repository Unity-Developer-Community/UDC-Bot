using Discord.Commands;
using DiscordBot.Service;
using DiscordBot.Services;
using DiscordBot.Settings;
using static DiscordBot.Service.CannedResponseService;

namespace DiscordBot.Modules;

public class CannedResponseModule : ModuleBase
{
    #region Dependency Injection
    
    public UserService UserService { get; set; }
    public BotSettings BotSettings { get; set; }
    public CannedResponseService CannedResponseService { get; set; }
    
    #endregion // Dependency Injection
    
    [Command("ask"), Alias("dontasktoask", "nohello")]
    [Summary("When someone asks to ask a question, respond with a link to the 'How to Ask' page.")]
    public async Task RespondWithHowToAsk()
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        var embed = CannedResponseService.GetCannedResponse(CannedResponseType.HowToAsk, Context.User);
        await Context.Message.DeleteAsync();
        
        await ReplyAsync(string.Empty, false, embed.Build());
    }
    
    [Command("paste")]
    [Summary("When someone asks how to paste code, respond with a link to the 'How to Paste Code' page.")]
    public async Task RespondWithHowToPaste()
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        var embed = CannedResponseService.GetCannedResponse(CannedResponseType.Paste, Context.User);
        await Context.Message.DeleteAsync();
        
        await ReplyAsync(string.Empty, false, embed.Build());
    }
    
    [Command("nocode")]
    [Summary("When someone asks for help with code, but doesn't provide any, respond with a link to the 'No Code Provided' page.")]
    public async Task RespondWithNoCode()
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        var embed = CannedResponseService.GetCannedResponse(CannedResponseType.NoCode, Context.User);
        await Context.Message.DeleteAsync();
        
        await ReplyAsync(string.Empty, false, embed.Build());
    }
    
    [Command("xy")]
    [Summary("When someone is asking about their attempted solution rather than their actual problem, respond with a link to the 'XY Problem' page.")]
    public async Task RespondWithXYProblem()
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        var embed = CannedResponseService.GetCannedResponse(CannedResponseType.XYProblem, Context.User);
        await Context.Message.DeleteAsync();
        
        await ReplyAsync(string.Empty, false, embed.Build());
    }
    
    [Command("biggame"), Alias("scope", "bigscope", "scopecreep")]
    [Summary("When someone is asking for help with a large project, respond with a link to the 'Game Too Big' page.")]
    public async Task RespondWithGameToBig()
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        var embed = CannedResponseService.GetCannedResponse(CannedResponseType.GameToBig, Context.User);
        await Context.Message.DeleteAsync();
        
        await ReplyAsync(string.Empty, false, embed.Build());
    }
    
    [Command("google"), Alias("search", "howtosearch")]
    [Summary("When someone asks a question that could have been answered by a quick search, respond with a link to the 'How to Google' page.")]
    public async Task RespondWithHowToGoogle()
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        var embed = CannedResponseService.GetCannedResponse(CannedResponseType.HowToGoogle, Context.User);
        await Context.Message.DeleteAsync();
        
        await ReplyAsync(string.Empty, false, embed.Build());
    }
    
    
}