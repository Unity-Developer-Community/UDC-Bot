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
        // General Help
        Programming,
        Art,
        ThreeD,
        TwoD,
        Audio,
        Design,
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

    private readonly EmbedBuilder _programmingEmbed = new EmbedBuilder
    {
        Title = "Programming Resources",
        Description = "Resources for programming, including languages, tools, and best practices.\n" +
                      "- Official Documentation [Manual](https://docs.unity3d.com/Manual/index.html) [Scripting API](https://docs.unity3d.com/ScriptReference/index.html)\n" +
                      "- Official Learn Pipeline [Unity Learn](https://learn.unity.com/)\n" +
                      "- Fundamentals: Unity [Roll-A-Ball](https://learn.unity.com/project/roll-a-ball)\n" +
                      "- Intermediate: Catlike Coding [Tutorials](https://catlikecoding.com/unity/tutorials/)\n" +
                      "- Best Practices: [Organizing Your Project](https://unity.com/how-to/organizing-your-project)\n" +
                      "- Design Patterns: [Game Programming Patterns](https://gameprogrammingpatterns.com/)",
        Url = "https://learn.unity.com/project/roll-a-ball"
    };
    
    private readonly EmbedBuilder _artEmbed = new EmbedBuilder
    {
        Title = "Art Resources",
        Description = "Resources for art\n" +
                      "- Learning Blender: [Blender Guru Donut](https://www.youtube.com/watch?v=TPrnSACiTJ4&list=PLjEaoINr3zgEq0u2MzVgAaHEBt--xLB6U&index=3)\n" +
                      "- Royalty Free Simple 2D/3D Assets: [Kenny](https://www.kenney.nl/assets)\n" +
                      "- Varying Assets: [Itch.io Royalty Free Assets](https://itch.io/game-assets/free/tag-royalty-free)\n" +
                      "- Blender Discord: [Server Invite](https://discord.gg/blender)"
    };
    
    private readonly EmbedBuilder _threeDEmbed = new EmbedBuilder
    {
        Title = "3D Resources",
        Description = "Resources for 3D\n" +
                      "- Learning Blender: [Blender Guru Donut](https://www.youtube.com/watch?v=TPrnSACiTJ4&list=PLjEaoINr3zgEq0u2MzVgAaHEBt--xLB6U&index=3)\n" +
                      "- Royalty Free Simple 3D Assets: [Kenny 3D](https://www.kenney.nl/assets/category:3D?sort=update)\n" +
                      "- Varying Assets: [Itch.io Royalty Free Assets](https://itch.io/game-assets/free/tag-3d/tag-royalty-free)\n" +
                      "- Blender Discord: [Server Invite](https://discord.gg/blender)"
    };
    
    private readonly EmbedBuilder _twoDEmbed = new EmbedBuilder
    {
        Title = "2D Resources",
        Description = "Resources for 2D\n" +
                      "- Royalty Free Simple 2D Assets: [Kenny 2D](https://www.kenney.nl/assets/category:2D?sort=update)\n" +
                      "- Varying Assets: [Itch.io Royalty Free Assets](https://itch.io/game-assets/free/tag-2d)\n" +
                      "- Blender Discord: [Server Invite](https://discord.gg/blender)"
    };
    
    private readonly EmbedBuilder _audioEmbed = new EmbedBuilder
    {
        Title = "Audio Resources",
        Description = "Resources for audio\n" +
                      "- Music (Attribute): [Incompetech](https://incompetech.com/)\n" +
                      "- Effects & Music: [Freesound](https://freesound.org/)\n" +
                      "- Effects & Music: [Itch.io](https://itch.io/game-assets/free/tag-music)\n" +
                      "- Audio Editor: [Audacity](https://www.audacityteam.org/)\n" +
                      "- Sound Design Explained: [PitchBlends](https://www.pitchbends.com/posts/what-is-sound-design)"
    };
    
    private readonly EmbedBuilder _designEmbed = new EmbedBuilder
    {
        Title = "Design Resources",
        Description = "Resources for design\n" +
                      "- Design Document: [David Fox](https://www.linkedin.com/pulse/free-game-design-doc-gdd-template-david-fox/)\n" +
                      "- Game Design: [Keep Things Clear](https://code.tutsplus.com/keep-things-clear-dont-confuse-your-players--cms-22780a)\n" +
                      "- Color Palettes: [Coolors](https://coolors.co/)\n" +
                      "- Font Pairing: [Font Pair](https://fontpair.co/)\n" +
                      "- Iconography: [Flaticon](https://www.flaticon.com/)\n" +
                      "- Free Icons: [Icon Monstr](https://iconmonstr.com/)"
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
            // General Help
            CannedResponseType.Programming => _programmingEmbed,
            CannedResponseType.Art => _artEmbed,
            CannedResponseType.ThreeD => _threeDEmbed,
            CannedResponseType.TwoD => _twoDEmbed,
            CannedResponseType.Audio => _audioEmbed,
            CannedResponseType.Design => _designEmbed,
            _ => null
        };
    }
}