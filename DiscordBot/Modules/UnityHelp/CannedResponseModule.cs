using Discord.Commands;
using DiscordBot.Attributes;
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
    
    // The core command for the canned response module
    public async Task RespondWithCannedResponse(CannedResponseType type)
    {
        if (Context.User.IsUserBotOrWebhook())
            return;
        
        var embed = CannedResponseService.GetCannedResponse(type, Context.User);
        await Context.Message.DeleteAsync();
        
        await ReplyAsync(string.Empty, false, embed.Build());
    }
    
    [Command("ask"), Alias("dontasktoask", "nohello")]
    [Summary("When someone asks to ask a question, respond with a link to the 'How to Ask' page.")]
    public async Task RespondWithHowToAsk()
    {
        await RespondWithCannedResponse(CannedResponseType.HowToAsk);
    }
    
    [Command("paste")]
    [Summary("When someone asks how to paste code, respond with a link to the 'How to Paste Code' page.")]
    public async Task RespondWithHowToPaste()
    {
        await RespondWithCannedResponse(CannedResponseType.Paste);
    }
    
    [Command("nocode")]
    [Summary("When someone asks for help with code, but doesn't provide any, respond with a link to the 'No Code Provided' page.")]
    public async Task RespondWithNoCode()
    {
        await RespondWithCannedResponse(CannedResponseType.NoCode);
    }
    
    [Command("xy")]
    [Summary("When someone is asking about their attempted solution rather than their actual problem, respond with a link to the 'XY Problem' page.")]
    public async Task RespondWithXYProblem()
    {
        await RespondWithCannedResponse(CannedResponseType.XYProblem);
    }
    
    [Command("biggame"), Alias("scope", "bigscope", "scopecreep")]
    [Summary("When someone is asking for help with a large project, respond with a link to the 'Game Too Big' page.")]
    public async Task RespondWithGameToBig()
    {
        await RespondWithCannedResponse(CannedResponseType.GameToBig);
    }
    
    [Command("google"), Alias("search", "howtosearch")]
    [Summary("When someone asks a question that could have been answered by a quick search, respond with a link to the 'How to Google' page.")]
    public async Task RespondWithHowToGoogle()
    {
        await RespondWithCannedResponse(CannedResponseType.HowToGoogle);
    }
    
    [Command("programming")]
    [Summary("When someone asks for programming resources, respond with a link to the 'Programming Resources' page.")]
    public async Task RespondWithProgrammingResources()
    {
        await RespondWithCannedResponse(CannedResponseType.Programming);
    }
    
    [Command("art")]
    [Summary("When someone asks for art resources, respond with a link to the 'Art Resources' page.")]
    public async Task RespondWithArtResources()
    {
        await RespondWithCannedResponse(CannedResponseType.Art);
    }
    
    [Command("3d"), Alias("3dmodeling", "3dassets")]
    [Summary("When someone asks for 3D modeling resources, respond with a link to the '3D Modeling Resources' page.")]
    public async Task RespondWith3DModelingResources()
    {
        await RespondWithCannedResponse(CannedResponseType.ThreeD);
    }
    
    [Command("2d"), Alias("2dmodeling", "2dassets")]
    [Summary("When someone asks for 2D modeling resources, respond with a link to the '2D Modeling Resources' page.")]
    public async Task RespondWith2DModelingResources()
    {
        await RespondWithCannedResponse(CannedResponseType.TwoD);
    }
    
    [Command("audio"), Alias("sound", "music")]
    [Summary("When someone asks for audio resources, respond with a link to the 'Audio Resources' page.")]
    public async Task RespondWithAudioResources()
    {
        await RespondWithCannedResponse(CannedResponseType.Audio);
    }
    
    [Command("design"), Alias("ui", "ux")]
    [Summary("When someone asks for design resources, respond with a link to the 'Design Resources' page.")]
    public async Task RespondWithDesignResources()
    {
        await RespondWithCannedResponse(CannedResponseType.Design);
    }
}