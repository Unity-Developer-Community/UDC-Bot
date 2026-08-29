using System.IO;
using DiscordBot.Settings.Legacy;
using Newtonsoft.Json;

namespace DiscordBot.Settings;

public static class StaticContentLoader
{
    public static T LoadRequired<T>(string path, string description)
    {
        if (!File.Exists(path))
            throw new BotConfigurationException($"Required {description} was not found at '{path}'.");

        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ??
                throw new JsonSerializationException("The root JSON value was null.");
        }
        catch (JsonException exception)
        {
            throw new BotConfigurationException(
                $"The {description} at '{path}' is malformed. The file was not changed. {exception.Message}",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BotConfigurationException($"Unable to read {description} at '{path}'.", exception);
        }
    }
}
