using System.IO;
using System.Security.Cryptography;
using DiscordBot.Settings;
using Newtonsoft.Json;

namespace DiscordBot.Services.Recruitment;

public sealed class TagDefinition
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public bool Closing { get; set; }
}
