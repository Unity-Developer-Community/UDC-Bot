using System.IO;
using Newtonsoft.Json;

namespace DiscordBot.Utils;

public static class SerializeUtil
{
    public static T DeserializeFile<T>(string path, bool newFileIfNotExists = true) where T : new()
    {
        if (!File.Exists(path))
        {
            if (newFileIfNotExists)
            {
                LoggingService.LogToConsole($@"Deserialized File at '{path}' does not exist, attempting to generate new file.",
                    LogSeverity.Warning);
                var deserializedItem = new T();
                File.WriteAllText(path, JsonConvert.SerializeObject(deserializedItem));
            }
            else
            {
                LoggingService.LogToConsole($@"Deserialized File at '{path}' does not exist.", LogSeverity.Error);
            }
        }

        try
        {
            using var file = File.OpenText(path);
            var content = JsonConvert.DeserializeObject<T>(file.ReadToEnd()) ?? new T();
            return content;
        }
        catch (JsonException ex)
        {
            LoggingService.LogToConsole(
                $"Corrupted JSON in '{path}': {ex.Message}. Backing up and resetting to default.", LogSeverity.Error);

            var backupPath = path + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
            try { File.Copy(path, backupPath, overwrite: true); }
            catch { /* best-effort backup */ }

            var fallback = new T();
            AtomicWriteText(path, JsonConvert.SerializeObject(fallback));
            return fallback;
        }
    }

    /// <summary> Tests objectToSerialize to confirm not null before saving it to path. </summary>
    public static bool SerializeFile<T>(string path, T objectToSerialize)
    {
        if (object.Equals(objectToSerialize, default(T)))
        {
            LoggingService.LogToConsole($"Object `{path}` passed into SerializeFile is null, ignoring save request.",
                LogSeverity.Warning);
            return false;
        }

        File.WriteAllText(path, JsonConvert.SerializeObject(objectToSerialize));
        return true;
    }

    public static async Task<bool> SerializeFileAsync<T>(string path, T objectToSerialize)
    {
        if (object.Equals(objectToSerialize, default(T)))
        {
            LoggingService.LogToConsole($"Object `{path}` passed into SerializeFile is null, ignoring save request.",
                LogSeverity.Warning);
            return false;
        }
        await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(objectToSerialize));
        return true;
    }

    public static async Task<T> LoadUrlDeserializeResult<T>(string url)
    {
        var result = await InternetExtensions.GetHttpContents(url);
        var resultObject = JsonConvert.DeserializeObject<T>(result);
        if (resultObject == null)
        {
            if (result?.Length > 400)
                result = result.Substring(0, 400) + "...";
            LoggingService.LogToConsole($"Failed to deserialize object from {url}\nContent: {result}", LogSeverity.Error);
        }
        return resultObject;
    }

}