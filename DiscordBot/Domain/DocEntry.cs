using Newtonsoft.Json;

namespace DiscordBot.Domain;

[JsonConverter(typeof(DocEntryJsonConverter))]
public record DocEntry(string PageName, string Title);

internal class DocEntryJsonConverter : JsonConverter<DocEntry>
{
    public override DocEntry? ReadJson(JsonReader reader, Type objectType, DocEntry? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.StartArray)
        {
            var arr = serializer.Deserialize<string[]>(reader);
            if (arr is { Length: >= 2 })
                return new DocEntry(arr[0], arr[1]);
            return null;
        }

        if (reader.TokenType == JsonToken.StartObject)
        {
            var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
            return new DocEntry(
                obj.Value<string>("PageName") ?? "",
                obj.Value<string>("Title") ?? "");
        }

        return null;
    }

    public override void WriteJson(JsonWriter writer, DocEntry? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }
        writer.WriteStartArray();
        writer.WriteValue(value.PageName);
        writer.WriteValue(value.Title);
        writer.WriteEndArray();
    }
}
