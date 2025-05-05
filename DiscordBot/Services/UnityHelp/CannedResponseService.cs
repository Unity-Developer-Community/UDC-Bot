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
        DeltaTime,
        // General Help
        Debugging,
        FolderStructure,
        // VersionControl,
        // ErrorMessages,
        // CodeStructure,
        // CodeComments,
        // Passive Aggressive
        GameTooBig,
        HowToGoogle,
        // Resources
        Programming,
        Art,
        ThreeD,
        TwoD,
        Audio,
        Design,
        // Animation,
        // Physics,
        // Networking,
        // PerformanceAndOptimization,
        // UIUX
    }

    public enum CannedHelp
    {
        HowToAsk = CannedResponseType.HowToAsk,
        CodePaste = CannedResponseType.Paste,
        NoCode = CannedResponseType.NoCode,
        XYProblem = CannedResponseType.XYProblem,
        DeltaTime = CannedResponseType.DeltaTime,
        // Mode general help
        Debugging = CannedResponseType.Debugging,
        FolderStructure = CannedResponseType.FolderStructure,
        // VersionControl = CannedResponseType.VersionControl,
        // ErrorMessages = CannedResponseType.ErrorMessages,
        // CodeStructure = CannedResponseType.CodeStructure,
        // CodeComments = CannedResponseType.CodeComments,
        // Passive Aggressive
        GameTooBig = CannedResponseType.GameTooBig,
        HowToGoogle = CannedResponseType.HowToGoogle,
    }
    
    public enum CannedResources
    {
        Programming = CannedResponseType.Programming,
        GeneralArt = CannedResponseType.Art,
        Art2D = CannedResponseType.ThreeD,
        Art3D = CannedResponseType.TwoD,
        Audio = CannedResponseType.Audio,
        Design = CannedResponseType.Design,
        // Animation = CannedResponseType.Animation,
        // Physics = CannedResponseType.Physics,
        // Networking = CannedResponseType.Networking,
        // PerformanceAndOptimization = CannedResponseType.PerformanceAndOptimization,
        // UIUX = CannedResponseType.UIUX
    }
    
    private readonly Color _defaultEmbedColor = new(0x00, 0x80, 0xFF);

    #region Canned Help
    
    private readonly EmbedBuilder _howToAskEmbed = new()
{
    Title = "How to Ask",
    Description = "When you have a question, just ask it directly and wait patiently for an answer. " + "Providing more information upfront can improve the quality and speed of the responses you receive. " + "There’s no need to ask for permission to ask or to check if someone is present before asking.\n" + "See: [How to Ask](https://stackoverflow.com/help/how-to-ask)",
    Url = "https://stackoverflow.com/help/how-to-ask",
};
    
    private readonly EmbedBuilder _pasteEmbed = new()
{
    Title = "How to Paste Code",
    // A unity based example
    Description = "When sharing code on Discord, it’s best to use code blocks. You can create a code block by wrapping your code in backticks (\\`\\`\\`). [Discord Markdown](https://gist.github.com/matthewzring/9f7bbfd102003963f9be7dbcf7d40e51#code-blocks)\n" + "```csharp\n" + "public void Start()\n" + "{\n" + "    Debug.Log(\"Hello, world!\");\n" + "    GameObject cube = GameObject.Instantiate(prefab);\n" + "    // Set the position of the cube to the origin\n" + "    cube.transform.position = new Vector3(0, 0, 0);\n" + "}\n" + "```\n" + "This will make your code easier to read and copy. If your code is too long, consider using a service like [GitHub Gist](https://gist.github.com/) or [Pastebin](https://pastebin.com/).",
    Url = "https://pastebin.com/",
};
    
    private readonly EmbedBuilder _noCodeEmbed = new()
{
    Title = "No Code Provided",
    Description = "***Where the code at?*** It appears you're trying to ask something that would benefit from showing what you've tried, but you haven't provided much code. " + "Someone who wants to help you won't be able to do so without seeing the code you're working with."
};
    
    private readonly EmbedBuilder _xyProblemEmbed = new()
{
    Title = "XY Problem",
    Description = "Don't ask about your attempted solution, include details about the actual problem you're trying to solve.\n" + "This leads to a lot of wasted time and energy, both on the part of people asking for help, and on the part of those providing help.\n" + "See: [XY Problem](https://xyproblem.info/)\n" + "- Always include information about the broader problem.\n" + "- If you've tried something, tell us what you tried",
    Url = "https://xyproblem.info/",
};
    
    private readonly EmbedBuilder _gameTooBigEmbed = new()
{
    Title = "Game Too Big",
    Description = "Managing project scope is important. It's important to start small and build up from there. " + "If you're new to game development, it's best to start with a small project to learn the basics. " + "Once you have a good understanding of the basics, you can start working on larger projects.\n" + "See: [Project Scope](https://clintbellanger.net/toobig/advice.html)",
};

    private readonly EmbedBuilder _howToGoogleEmbed = new()
{
    Title = "How to Google",
    Description = "Someone thinks this question could be answered by a quick search!\n" + "Quick searches often answer questions.\nAs developers, self-reliance in finding answers saves time.\n" + "See: [How to Google](https://www.lifehack.org/articles/technology/20-tips-use-google-search-efficiently.html)",
    Url = "https://www.lifehack.org/articles/technology/20-tips-use-google-search-efficiently.html",
};
    
    private readonly EmbedBuilder _deltaTime = new()
{
    Title = "Frame Independence",
    Description = "[Time.deltaTime](https://docs.unity3d.com/ScriptReference/Time-deltaTime.html) is the time in seconds it took to complete the last frame.\n" + "Avoid moving objects or making calculations based on constant values." + "```cs\n" + "var speed = 1.0f\n" + "var dir = transform.forward;\n" + "// Move 'speed' units forward **Per Frame**\n" + "transform.position += dir * speed;\n" + "// Move 'speed' units forward **Per Second**\n" + "transform.position += dir * speed * Time.deltaTime;```" + "Avoid per-frame adjustments as FPS varies among players, affecting object speed. Use `deltaTime` " + "[Update](https://docs.unity3d.com/ScriptReference/MonoBehaviour.Update.html) or " + "`fixedDeltaTime` [FixedUpdate](https://docs.unity3d.com/ScriptReference/MonoBehaviour.FixedUpdate.html) for consistent speed.\n" + "See: [Time Frame Management](https://docs.unity3d.com/Manual/TimeFrameManagement.html), " + "[FixedUpdate](https://docs.unity3d.com/ScriptReference/MonoBehaviour.FixedUpdate.html), " + "[DeltaTime](https://docs.unity3d.com/ScriptReference/Time-deltaTime.html)",
    Url = "https://docs.unity3d.com/Manual/TimeFrameManagement.html",
};
    
    private readonly EmbedBuilder _debugging = new()
{
    Title = "Debugging in Unity",
    Description = "Debugging is key in game development, and usually required to fix 'bugs' in code. This can often be the most time consuming work involved with programming.\n__Here are some Unity debugging tips:__\n" + "- **Error Messages**: Unity error messages indicate the error line and code (e.g. **CS1002**). [C# Compiler Errors](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs1002).\n" + "- **Debug.Log**: Unity [Debug.Log](https://docs.unity3d.com/ScriptReference/Debug.Log.html) to print console messages for code tracking and variable values.\n" + "- **Breakpoints**: Use [Breakpoints](https://docs.unity3d.com/Manual/ManagedCodeDebugging.html) to pause and step through code line by line at a specific point for game state examination.\n" + "- **Unity Profiler**: The [Unity Profiler](https://docs.unity3d.com/Manual/Profiler.html) identifies performance bottlenecks for game optimization.\n" + "- **Documentation**: Unity's documentation provides insights into functions, features, and error resolution.\n" + "Debugging improves with practice, enhancing your bug identification and resolution skills.",
    Url = "https://docs.unity3d.com/Manual/ManagedCodeDebugging.html",
};
    
    private readonly EmbedBuilder _folderStructure = new()
{
    Title = "Folder Structure",
    Description = "Organizing your project is important for maintainability and collaboration (Including yourself weeks later). + " + "well-organized project is easier to navigate and understand, and makes it easier to find and fix problems.\n" + "- **Consistency**: Keep it consistent, and make sure everyone on your team knows the structure.\n" + "- **Separation**: Keep your assets separate from your code.\n" + "- **Naming**: Use clear and consistent naming conventions.\n" + "- **Documentation**: Keep a README file, either in the root of project, or in specific folders when additional information would be useful.\n" + "See: [Organizing Your Project](https://unity.com/how-to/organizing-your-project)",
    Url = "https://unity.com/how-to/organizing-your-project",
};
    
    #endregion

    #region Canned Resources
    
    private readonly EmbedBuilder _programmingEmbed = new()
{
    Title = "Programming Resources",
    Description = "Resources for programming, including languages, tools, and best practices.\n" + "- Official Documentation [Manual](https://docs.unity3d.com/Manual/index.html) [Scripting API](https://docs.unity3d.com/ScriptReference/index.html)\n" + "- Official Learn Pipeline [Unity Learn](https://learn.unity.com/)\n" + "- Fundamentals: Unity [Roll-A-Ball](https://learn.unity.com/project/roll-a-ball)\n" + "- Intermediate: Catlike Coding [Tutorials](https://catlikecoding.com/unity/tutorials/)\n" + "- Best Practices: [Organizing Your Project](https://unity.com/how-to/organizing-your-project)\n" + "- Design Patterns: [Game Programming Patterns](https://gameprogrammingpatterns.com/)",
    Url = "https://learn.unity.com/project/roll-a-ball"
};
    
    private readonly EmbedBuilder _artEmbed = new()
{
    Title = "Art Resources",
    Description = "Resources for art\n" + "- Learning Blender: [Blender Guru Donut](https://www.youtube.com/watch?v=TPrnSACiTJ4&list=PLjEaoINr3zgEq0u2MzVgAaHEBt--xLB6U&index=3)\n" + "- Royalty Free Simple 2D/3D Assets: [Kenny](https://www.kenney.nl/assets)\n" + "- Varying Assets: [Itch.io Royalty Free Assets](https://itch.io/game-assets/free/tag-royalty-free)\n" + "- Blender Discord: [Server Invite](https://discord.gg/blender)"
};
    
    private readonly EmbedBuilder _threeDEmbed = new()
{
    Title = "3D Resources",
    Description = "Resources for 3D\n" + "- Learning Blender: [Blender Guru Donut](https://www.youtube.com/watch?v=TPrnSACiTJ4&list=PLjEaoINr3zgEq0u2MzVgAaHEBt--xLB6U&index=3)\n" + "- Royalty Free Simple 3D Assets: [Kenny 3D](https://www.kenney.nl/assets/category:3D?sort=update)\n" + "- Varying Assets: [Itch.io Royalty Free Assets](https://itch.io/game-assets/free/tag-3d/tag-royalty-free)\n" + "- Blender Discord: [Server Invite](https://discord.gg/blender)"
};
    
    private readonly EmbedBuilder _twoDEmbed = new()
{
    Title = "2D Resources",
    Description = "Resources for 2D\n" + "- Royalty Free Simple 2D Assets: [Kenny 2D](https://www.kenney.nl/assets/category:2D?sort=update)\n" + "- Varying Assets: [Itch.io Royalty Free Assets](https://itch.io/game-assets/free/tag-2d)\n" + "- Blender Discord: [Server Invite](https://discord.gg/blender)"
};
    
    private readonly EmbedBuilder _audioEmbed = new()
{
    Title = "Audio Resources",
    Description = "Resources for audio\n" + "- Music (Attribute): [Incompetech](https://incompetech.com/)\n" + "- Effects & Music: [Freesound](https://freesound.org/)\n" + "- Effects & Music: [Itch.io](https://itch.io/game-assets/free/tag-music)\n" + "- Audio Editor: [Audacity](https://www.audacityteam.org/)\n" + "- Sound Design Explained: [PitchBlends](https://www.pitchbends.com/posts/what-is-sound-design)"
};
    
    private readonly EmbedBuilder _designEmbed = new()
{
    Title = "Design Resources",
    Description = "Resources for design\n" + "- Design Document: [David Fox](https://www.linkedin.com/pulse/free-game-design-doc-gdd-template-david-fox/)\n" + "- Game Design: [Keep Things Clear](https://code.tutsplus.com/keep-things-clear-dont-confuse-your-players--cms-22780a)\n" + "- Color Palettes: [Coolors](https://coolors.co/)\n" + "- Font Pairing: [Font Pair](https://fontpair.co/)\n" + "- Iconography: [Flaticon](https://www.flaticon.com/)\n" + "- Free Icons: [Icon Monstr](https://iconmonstr.com/)"
};
    
    #endregion
    
    #endregion // Configuration
    
    public EmbedBuilder GetCannedResponse(CannedResponseType type, IUser requestor = null)
    {
        var embed = GetUnbuiltCannedResponse(type);
        if (embed == null)
            return null;
        
        if (requestor != null)
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
            CannedResponseType.DeltaTime => _deltaTime,
            // General Help
            CannedResponseType.Debugging => _debugging,
            CannedResponseType.FolderStructure => _folderStructure,
            // CannedResponseType.VersionControl =>
            // CannedResponseType.ErrorMessages =>
            // CannedResponseType.CodeStructure =>
            // CannedResponseType.CodeComments =>
            // Passive Aggressive
            CannedResponseType.GameTooBig => _gameTooBigEmbed,
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