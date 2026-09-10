namespace DiscordBot.Data;

public class Rating
{
    public object Count { get; set; } = null!;
    public int Average { get; set; }
}

public class Kategory
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public class Category
{
    public string TreeId { get; set; } = string.Empty;
    public string LabelEnglish { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Multiple { get; set; } = string.Empty;
}

public class Publisher
{
    public string LabelEnglish { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public object SupportUrl { get; set; } = null!;
}

public class Link
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public class List
{
    public string Slug { get; set; } = string.Empty;
    public string SlugV2 { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public object Overlay { get; set; } = null!;
}

public class Flags
{
}

public class Image
{
    public string Link { get; set; } = string.Empty;
    public string Width { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Height { get; set; } = string.Empty;
    public string Thumb { get; set; } = string.Empty;
}

public class Keyimage
{
    public string Small { get; set; } = string.Empty;
    public string Big { get; set; } = string.Empty;
    public object SmallLegacy { get; set; } = null!;
    public object Facebook { get; set; } = null!;
    public object BigLegacy { get; set; } = null!;
    public string Icon { get; set; } = string.Empty;
    public string Icon75 { get; set; } = string.Empty;
    public string Icon25 { get; set; } = string.Empty;
}

public class Daily
{
    public string Icon { get; set; } = string.Empty;
    public Rating Rating { get; set; } = null!;
    public int Remaining { get; set; }
    public Kategory Kategory { get; set; } = null!;
    public string PackageVersionId { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Category Category { get; set; } = null!;
    public string Hotness { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public Publisher Publisher { get; set; } = null!;
    public List<object> List { get; set; } = [];
    public Link Link { get; set; } = null!;
    public Flags Flags { get; set; } = null!;
    public Keyimage Keyimage { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public string TitleEnglish { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class Content
{
    public string Pubdate { get; set; } = string.Empty;
    public string MinUnityVersion { get; set; } = string.Empty;
    public Rating Rating { get; set; } = null!;
    public Kategory Kategory { get; set; } = null!;
    public List<string> UnityVersions { get; set; } = [];
    public string Url { get; set; } = string.Empty;
    public string PackageVersionId { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Category Category { get; set; } = null!;
    public string Id { get; set; } = string.Empty;
    public Publisher Publisher { get; set; } = null!;
    public string Sizetext { get; set; } = string.Empty;
    public List<object> List { get; set; } = [];
    public Link Link { get; set; } = null!;
    public List<Image> Images { get; set; } = [];
    public Flags Flags { get; set; } = null!;
    public string Version { get; set; } = string.Empty;
    public string FirstPublishedAt { get; set; } = string.Empty;
    public Keyimage Keyimage { get; set; } = null!;
    public int License { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<object> Upgrades { get; set; } = [];
    public string Publishnotes { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ShortUrl { get; set; } = string.Empty;
    public List<object> Upgradables { get; set; } = [];
}

public class DailyObject
{
    public string Banner { get; set; } = string.Empty;
    public string Feed { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DaysLeft { get; set; }
    public int Total { get; set; }
    public Daily Daily { get; set; } = null!;
    public int Remaining { get; set; }
    public string Badge { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Countdown { get; set; }
    public List<object> Results { get; set; } = [];
}

public class PackageObject
{
    public Content Content { get; set; } = null!;
}

public class PriceObject
{
    public string Vat { get; set; } = string.Empty;
    public string PriceExvat { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public bool IsFree { get; set; }
}

public class Result
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
}

public class PackageHeadObject
{
    public Result Result { get; set; } = null!;
}