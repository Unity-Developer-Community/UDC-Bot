﻿using System;
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
                ConsoleLogger.Log($@"Deserialized File at '{path}' does not exist, attempting to generate new file.", Severity.Warning);
                deserializedItem = new T();
                SaveContents(path, JsonConvert.SerializeObject(deserializedItem));
            }
            else
            {
                ConsoleLogger.Log($@"Deserialized File at '{path}' does not exist.", Severity.Error);
            }
        }

        using (var file = File.OpenText(path))
        {
            deserializedItem = JsonConvert.DeserializeObject<T>(file.ReadToEnd());
        }

        // Returns the item which could be null.
        return deserializedItem;
    }

    public static bool SerializeFile<T>(string path, T objectToSerialize)
    {
        if (objectToSerialize == null)
        {
            ConsoleLogger.Log($"Object passed into SerializeFile \"{path}\" is null, ignoring save request.", Severity.Warning);
            return false;
        }
        SaveContents(path, JsonConvert.SerializeObject(objectToSerialize));
        return true;
    }

    private static void SaveContents(string path, string contents)
    {
        try
        {
            File.WriteAllText(path, contents);
            ConsoleLogger.Log($"Saved contents of {path}.", Severity.Info);
        }
        catch (Exception ex)
        {
            ConsoleLogger.Log($"Failed to save file \"{ path }\". \nErr: {ex.Message}\nTrace: {ex.StackTrace}");
            throw;
        }
    }
}