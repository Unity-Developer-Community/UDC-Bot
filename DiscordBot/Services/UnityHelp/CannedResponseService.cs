namespace DiscordBot.Service;

public class CannedResponseService
{
    private const string ServiceName = "CannedResponseService";
    
    #region Configuration
    
    public enum CannedResponseType
    {
        HowToAsk,
        Paste,
        NoCode,
        XYProblem,
        // Passive Aggressive
        GameToBig,
        HowToGoogle,
    }
    
    private readonly Color _defaultEmbedColor = new Color(0x00, 0x80, 0xFF);

    private readonly EmbedBuilder _howToAskEmbed = new EmbedBuilder
    {
        Title = "How to Ask",
        Description = "When you have a question, just ask it directly and wait patiently for an answer. " +
                      "Providing more information upfront can improve the quality and speed of the responses you receive. " +
                      "There’s no need to ask for permission to ask or to check if someone is present before asking.\n" +
                      "See: [How to Ask](https://stackoverflow.com/help/how-to-ask)",
        Url = "https://stackoverflow.com/help/how-to-ask",
    };
    
    private readonly EmbedBuilder _pasteEmbed = new EmbedBuilder
    {
        Title = "How to Paste Code",
        // A unity based example
        Description = "When sharing code on Discord, it’s best to use code blocks. You can create a code block by wrapping your code in backticks (\\`\\`\\`). For example:\n" +
                      "```csharp\n" +
                      "public void Start()\n" +
                      "{\n" +
                      "    Debug.Log(\"Hello, world!\");\n" +
                      "    GameObject cube = GameObject.Instantiate(prefab);\n" +
                      "    // Set the position of the cube to the origin\n" +
                      "    cube.transform.position = new Vector3(0, 0, 0);\n" +
                      "}\n" +
                      "```\n" +
                      "This will make your code easier to read and copy. If your code is too long, consider using a service like [GitHub Gist](https://gist.github.com/) or [Pastebin](https://pastebin.com/).",
        Url = "https://pastebin.com/",
    };
    
    private readonly EmbedBuilder _noCodeEmbed = new EmbedBuilder
    {
        Title = "No Code Provided",
        Description = "***Where the code at?*** Your question is code focused, but you haven't provided much if any of the code involved." +
                      "Someone who wants to help you won't be able to do so without seeing the code you're working with."
    };
    
    private readonly EmbedBuilder _xyProblemEmbed = new EmbedBuilder
    {
        Title = "XY Problem",
        Description = "Don't ask about your attempted solution, ask about the actual problem.\n" +
                      "This leads to a lot of wasted time and energy, both on the part of people asking for help, and on the part of those providing help.\n" +
                      "See: [XY Problem](https://xyproblem.info/)\n" +
                      "- Always include information about the broader problem.\n" +
                      "- If you've tried something, tell us what you tried",
        Url = "https://xyproblem.info/",
    };
    
    private readonly EmbedBuilder _gameToBigEmbed = new EmbedBuilder
    {
        Title = "Game Too Big",
        Description = "Managing project scope is important. It's important to start small and build up from there. " +
                      "If you're new to game development, it's best to start with a small project to learn the basics. " +
                      "Once you have a good understanding of the basics, you can start working on larger projects.\n" +
                      "See: [Project Scope](https://clintbellanger.net/toobig/advice.html)",
    };
    
    private readonly EmbedBuilder _howToGoogleEmbed = new EmbedBuilder
    {
        Title = "How to Google",
        Description = "Someone thinks this question could be answered by a quick search!\n" +
                      "Quick searches often answer questions.\nAs developers, self-reliance in finding answers saves time.\n" +
                      "See: [How to Google](https://www.lifehack.org/articles/technology/20-tips-use-google-search-efficiently.html)",
        Url = "https://www.lifehack.org/articles/technology/20-tips-use-google-search-efficiently.html",
    };
    
    #endregion // Configuration
    
    public EmbedBuilder GetCannedResponse(CannedResponseType type, IUser requestor)
    {
        var embed = GetUnbuiltCannedResponse(type);
        if (embed == null)
            return null;
        
        embed.FooterRequestedBy(requestor);
        embed.WithColor(_defaultEmbedColor);
        
        return embed;
    }
    
    public EmbedBuilder GetUnbuiltCannedResponse(CannedResponseType type)
    {
        return type switch
        {
            CannedResponseType.HowToAsk => _howToAskEmbed,
            CannedResponseType.Paste => _pasteEmbed,
            CannedResponseType.NoCode => _noCodeEmbed,
            CannedResponseType.XYProblem => _xyProblemEmbed,
            // Passive Aggressive
            CannedResponseType.GameToBig => _gameToBigEmbed,
            CannedResponseType.HowToGoogle => _howToGoogleEmbed,
            _ => null
        };
    }
}