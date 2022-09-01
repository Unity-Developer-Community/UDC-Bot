using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Skin;

public class SkinModuleJsonConverter : JsonConverter
{
    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType) => objectType == typeof(ISkinModule);

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        Type type;
        try
        {
            var t = $"DiscordBot.Skin.{jo["Type"].Value<string>()}SkinModule";
            type = Type.GetType(t);
            return jo.ToObject(type);
        }
        catch (Exception e)
        {
            LoggingService.LogToConsole($"{e.ToString()}", LogSeverity.Error);
            throw;
        }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}