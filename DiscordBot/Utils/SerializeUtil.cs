using System;
using System.IO;
using DiscordBot.Services;
using Newtonsoft.Json;

public class SerializeUtil
{
    public static T DeserializeFile<T>(string path, bool newFileIfNotExists = true) where T : new()
    {
        T deserializedItem;
        // Check if file exists,
        if (!File.Exists(path))
        {
            if (newFileIfNotExists)
            {
                Console.WriteLine($@"Deserialized File at '{path}' does not exist, attempting to generate new file.");
                deserializedItem = new T();
                var newFileContents = JsonConvert.SerializeObject(deserializedItem);
                File.WriteAllText(path, newFileContents);
            }
            else
            {
                Console.WriteLine($@"Deserialized File at '{path}' does not exist.");
            }
        }

        using (var file = File.OpenText(path))
        {
            deserializedItem = JsonConvert.DeserializeObject<T>(file.ReadToEnd());
        }

        // Returns the item which could be null.
        return deserializedItem;
    }
}