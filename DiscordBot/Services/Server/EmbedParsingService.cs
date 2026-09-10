using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace DiscordBot.Services.Server;

public class EmbedParsingService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public EmbedParsingService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

#pragma warning disable 0649
    private class EmbedData
    {
        public class Footer
        {
            public string icon_url = string.Empty;
            public string text = string.Empty;
        }

        public class Thumbnail
        {
            public string url = string.Empty;
        }

        public class Image
        {
            public string url = string.Empty;
        }

        public class Author
        {
            public string name = string.Empty;
            public string url = string.Empty;
            public string icon_url = string.Empty;
        }

        public class Field
        {
            public string name = string.Empty;
            public string value = string.Empty;
            public bool? inline;
        }

        public string title = string.Empty;
        public string description = string.Empty;
        public string url = string.Empty;
        public uint? color;
        public DateTimeOffset? timestamp;
        public Footer footer = null!;
        public Thumbnail thumbnail = null!;
        public Image image = null!;
        public Author author = null!;
        public Field[] fields = [];
    }
#pragma warning restore 0649

    private static readonly string[] ValidHosts =
    {
        "hastebin.com", "gdl.space", "hastepaste.com", "pastebin.com", "pastie.org"
    };

    public bool IsValidHost(string host) => ValidHosts.Contains(host);

    public string GetDownloadUrl(Uri uri)
    {
        return uri.Host switch
        {
            "hastebin.com" or "gdl.space" => $"https://{uri.Host}/raw{uri.AbsolutePath}",
            "hastepaste.com" => $"https://hastepaste.com/raw{uri.AbsolutePath[5..]}",
            "pastebin.com" => $"https://pastebin.com/raw{uri.AbsolutePath}",
            "pastie.org" => $"{uri.OriginalString}/raw",
            _ => string.Empty
        };
    }

    public async Task<Discord.Embed> BuildEmbedFromUrl(string url)
    {
        using var client = _httpClientFactory.CreateClient();
        var buffer = await client.GetByteArrayAsync(url);
        string json = Encoding.UTF8.GetString(buffer);
        return BuildEmbed(json);
    }

    public Discord.Embed BuildEmbed(string json)
    {
        var embedData = JsonConvert.DeserializeObject<EmbedData>(json);
        var builder = new Discord.EmbedBuilder();
        if (embedData == null) return builder.Build();

        if (!string.IsNullOrEmpty(embedData.title)) builder.Title = embedData.title;
        if (!string.IsNullOrEmpty(embedData.description)) builder.Description = embedData.description;
        if (!string.IsNullOrEmpty(embedData.url)) builder.Url = embedData.url;
        if (embedData.color.HasValue) builder.Color = new Discord.Color(embedData.color.Value);
        if (embedData.timestamp.HasValue) builder.Timestamp = embedData.timestamp.Value;

        if (embedData.footer != null)
        {
            builder.Footer = new Discord.EmbedFooterBuilder();
            if (!string.IsNullOrEmpty(embedData.footer.icon_url)) builder.Footer.IconUrl = embedData.footer.icon_url;
            if (!string.IsNullOrEmpty(embedData.footer.text)) builder.Footer.Text = embedData.footer.text;
        }

        if (embedData.thumbnail != null && !string.IsNullOrEmpty(embedData.thumbnail.url))
            builder.ThumbnailUrl = embedData.thumbnail.url;
        if (embedData.image != null && !string.IsNullOrEmpty(embedData.image.url))
            builder.ImageUrl = embedData.image.url;

        if (embedData.author != null)
        {
            builder.Author = new Discord.EmbedAuthorBuilder();
            if (!string.IsNullOrEmpty(embedData.author.icon_url)) builder.Author.IconUrl = embedData.author.icon_url;
            if (!string.IsNullOrEmpty(embedData.author.name)) builder.Author.Name = embedData.author.name;
            if (!string.IsNullOrEmpty(embedData.author.url)) builder.Author.Url = embedData.author.url;
        }

        if (embedData.fields != null)
        {
            foreach (var field in embedData.fields)
            {
                var f = new Discord.EmbedFieldBuilder();
                if (!string.IsNullOrEmpty(field.name)) f.Name = field.name;
                if (!string.IsNullOrEmpty(field.value)) f.Value = field.value;
                if (field.inline.HasValue) f.IsInline = field.inline.Value;
                builder.AddField(f);
            }
        }

        return builder.Build();
    }
}
